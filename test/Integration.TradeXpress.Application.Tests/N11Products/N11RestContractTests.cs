using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST yolunun SÖZLEŞME testleri — <b>ağ YOK, DI YOK</b>. Girdi olarak resmî dokümanın
/// (<c>n11-api-v9_0-duzmetin.txt</c>) kendi örnek body'leri/kuralları kullanılır; üçüncü taraf kaynak yoktur.
///
/// <para><b>Bu sınıfın varlık sebebi:</b> N11'in üç yazma ucu (<c>product-create</c> · <c>product-update</c> ·
/// <c>price-stock-update</c>) ASENKRONDUR — HTTP 200 "başarı" değil "kuyruğa alındı" demektir. Biçim hatası
/// ağa çıktığında hemen görünmez; <c>taskId</c> pollandığında REJECT olarak geri döner. Yani yanlış kurulmuş bir
/// istek sessizce bir senkron turunu (ve 1000 SKU'luk bir partiyi) yakar. Buradaki her [Fact] dokümanın
/// belgelenmiş bir REJECT sebebini ya da sessiz no-op'unu ağa çıkmadan yakalamak içindir.</para>
///
/// <para><b>Kilitlenen kurallar:</b> gönderilmeyen alanın JSON'a HİÇ yazılmaması (stok-only güncellemenin temeli) ·
/// listPrice+salePrice birlikteliği · listPrice ≥ salePrice · fiyatın NOKTA ayraçlı ve tam 2 haneli olması
/// (tr-TR kültürü aktifken bile) · 1000 SKU parti sınırı · task statülerinin fail-closed eşlenmesi ·
/// product-query <c>size</c> tavanının 50'ye kırpılması · <c>n11ProductId</c>'nin <c>long</c> olması.</para>
///
/// <para><b>Erişim yolu — neden böyle:</b> istemcinin doğrulama/serileştirme/dilimleme adımları <c>private</c>'tır
/// (kapsülleme doğrudur, test uğruna delinmez). Bu yüzden:
/// <list type="bullet">
///   <item><b>Guard'lar</b> PUBLIC async uçtan sınanır — doğrulama HTTP'den ÖNCE koştuğu için istisna ağa
///   çıkmadan gelir (<c>UpdatePriceStockAsync</c> → <c>ValidatePriceStocks</c> → ancak sonra gönderim).</item>
///   <item><b>Bir satırın GEÇERLİ olduğunu</b> ağa çıkmadan kanıtlamanın yolu "tel tuzağı"dır: geçerli satırın
///   ARDINA kasten bozuk bir satır konur; hata KODU + <c>StockCode</c>'u ikinci satıra aitse birincisi tüm
///   guard'lardan geçmiş demektir (doğrulama listeyi sırayla gezer).</item>
///   <item><b>JSON body'si</b> modelin kendisi serialize edilerek sınanır; serileştirme sözleşmesi tabanın
///   SSOT'undan (<see cref="N11RestClientBase"/>) okunur, testte yeniden yazılmaz.</item>
/// </list></para>
///
/// <para><b>Kapsam dışı (bilinçli):</b> <c>ParseSubmission</c> / <c>N11TaskPoller.QueryAsync</c> /
/// <c>N11ProductQueryClient.ParsePage</c> yanıt ayrıştırmaları ağ ya da private erişim istediğinden burada
/// sınanmaz; statü yorumunun SSOT'u olan <see cref="N11TaskStates"/> doğrudan test edilir.</para>
/// </summary>
public class N11RestContractTests
{
    /// <summary>Uç adresleri artık yapılandırılabilir (<see cref="N11EndpointOptions"/>); testler VARSAYILAN
    /// tabanla koşar — sözleşme kuralları (kırpma, kaçışlama, zorunlu alanlar) adresten bağımsızdır.</summary>
    private static readonly IOptions<N11EndpointOptions> Endpoints = Options.Create(new N11EndpointOptions());

    /// <summary>product-query taban adresi — <c>BuildUrl</c> saf statik kaldığı için parametre olarak verilir.</summary>
    private static readonly string QueryBase = new N11EndpointOptions().RestQueryBase;

    private readonly IN11ProductRestClient _client = new N11ProductRestClient(
        NullLogger<N11ProductRestClient>.Instance, Endpoints);

    // Guard'lar HTTP'den önce koştuğu için kimlik değerleri hiç kullanılmaz — sahte veriyoruz.
    private const string FakeAppKey = "test";
    private const string FakeAppSecret = "test";

    /// <summary>
    /// Tabanın ORTAK JSON sözleşmesine (<c>protected static JsonOptions</c>) test tarafından erişmenin tek yolu
    /// türemektir. Böylece camelCase + "null yazma" kuralları testte YENİDEN TANIMLANMAZ; gerçek SSOT okunur.
    /// </summary>
    private sealed class RestJsonProbe : N11RestClientBase
    {
        // Taban artık endpoint adresi ister. Probe yalnız STATİK JsonOptions'a erişmek için var, hiç örneklenmiyor —
        // ctor sadece derlemenin geçmesi için; varsayılan adres yeterli (JSON sözleşmesi adresten bağımsız).
        public RestJsonProbe()
            : base(Options.Create(new N11EndpointOptions()))
        {
        }

        public static JsonSerializerOptions BaseOptions => JsonOptions;
    }

    /// <summary>
    /// Yazma body'sinin serileştirme sözleşmesi — istemcinin <c>WriteJsonOptions</c> bileşimiyle BİREBİR aynı:
    /// taban sözleşme + fiyat dönüştürücüsü. Kopyalanan tek şey converter kaydıdır; kuralların kendisi tabandan gelir.
    /// </summary>
    private static readonly JsonSerializerOptions WriteJson =
        new(RestJsonProbe.BaseOptions) { Converters = { new N11PriceJsonConverter() } };

    // ── 1) price-stock body'si: gönderilmeyen alan GÜNCELLENMEZ ────────────────────────────────────

    [Fact]
    public void Stock_only_update_omits_the_price_keys_entirely()
    {
        // BU TESTİN VARLIK SEBEBİ: doküman birebir — "İstekte mevcut olmayan alanlar için herhangi bir update
        // yapılmayacaktır." Bu, stok-only güncellemenin TEK mekanizmasıdır. null bir fiyat JSON'a "listPrice": null
        // diye yazılırsa N11 bunu "alan gönderildi" sayabilir; en iyi ihtimalle REJECT, en kötüsünde ürünün
        // fiyatını sıfırlar. Anahtar HİÇ bulunmamalı — değeri null olmamalı.
        var json = SerializePriceStockBody(
            new N11RestPriceStock("ALT-KLY-14", ListPrice: null, SalePrice: null, Quantity: 7, CurrencyType: null));

        var sku = Skus(json).Single();

        sku.TryGetProperty("listPrice", out _).ShouldBeFalse();
        sku.TryGetProperty("salePrice", out _).ShouldBeFalse();
        sku.TryGetProperty("currencyType", out _).ShouldBeFalse();

        sku.GetProperty("stockCode").GetString().ShouldBe("ALT-KLY-14");
        sku.GetProperty("quantity").GetInt32().ShouldBe(7);
    }

    [Fact]
    public void Price_only_update_omits_the_quantity_key()
    {
        // Aynı kuralın diğer yönü: fiyat güncellerken stok gönderilmezse N11'deki stok DOKUNULMAZ. Bu, satış
        // sırasında N11'in kendi düşürdüğü stoğu yerel (bayat) sayımızla geri yazmamamızı sağlar.
        var json = SerializePriceStockBody(
            new N11RestPriceStock("ALT-KLY-14", ListPrice: 1200m, SalePrice: 1100m, Quantity: null, CurrencyType: "TL"));

        var sku = Skus(json).Single();

        sku.TryGetProperty("quantity", out _).ShouldBeFalse();
        sku.GetProperty("currencyType").GetString().ShouldBe("TL");
    }

    [Fact]
    public void Payload_is_wrapped_in_the_documented_envelope()
    {
        // Body şekli {"payload":{"integrator":..,"skus":[..]}} — düz bir dizi göndermek 4xx üretir. integrator
        // dokümanda ZORUNLU ve "tüm gönderimlerinizde aynı değer" isteniyor; boş bırakılırsa istek reddedilir.
        var json = SerializePriceStockBody(
            new N11RestPriceStock("A", null, null, 1, null),
            new N11RestPriceStock("B", null, null, 2, null));

        using var doc = JsonDocument.Parse(json);

        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "payload" });
        var payload = doc.RootElement.GetProperty("payload");
        payload.GetProperty("integrator").GetString().ShouldNotBeNullOrWhiteSpace();
        payload.GetProperty("skus").GetArrayLength().ShouldBe(2);
    }

    // ── 2) listPrice + salePrice BİRLİKTE ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sale_price_without_a_list_price_is_rejected_before_the_call()
    {
        // Doküman birebir: "Fiyat güncellemelerinde listPrice ve salePrice parametreleri birlikte gönderilmelidir."
        // Tek başına gönderilirse N11 partiyi REJECT eder — ama bunu ancak taskId pollandığında öğreniriz.
        // Yerelde durmak, 1000 SKU'luk bir turu kurtarır (istisna ağa çıkmadan, gönderimden ÖNCE gelir).
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[]
            {
                new N11RestPriceStock("ALT-KLY-14", ListPrice: null, SalePrice: 1600m, Quantity: null, CurrencyType: "TL"),
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:PriceFieldsMustBePaired");
        ex.Data["StockCode"].ShouldBe("ALT-KLY-14");   // hangi satırın suçlu olduğu log'dan okunabilmeli
    }

    [Fact]
    public async Task List_price_without_a_sale_price_is_rejected_too()
    {
        // Kural simetrik: eksik olan hangi taraf olursa olsun istek eksik bir fiyat çiftidir.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[]
            {
                new N11RestPriceStock("ALT-KLY-14", ListPrice: 1600m, SalePrice: null, Quantity: null, CurrencyType: "TL"),
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:PriceFieldsMustBePaired");
    }

    // ── 3) listPrice ≥ salePrice ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_price_below_sale_price_is_rejected()
    {
        // Doküman birebir: "listPrice, salePrice'dan yüksek olmalıdır. Aksi takdirde isteğiniz REJECT alacaktır."
        // Kuyum bağlamında bu kolayca oluşur: PSF sabit bırakılıp altın kuru yükseldiğinde TSF onu geçer.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[]
            {
                new N11RestPriceStock("ALT-KLY-14", ListPrice: 1500m, SalePrice: 1600m, Quantity: null, CurrencyType: "TL"),
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:ListPriceBelowSalePrice");
    }

    [Fact]
    public async Task Stock_only_and_equal_price_rows_pass_the_guards()
    {
        // İKİ kural tek testte, "tel tuzağı" tekniğiyle (ağa çıkmadan GEÇERLİLİK kanıtı):
        //   · stok-only satır (fiyat yok) geçerlidir — fiyat çifti kuralı yalnız fiyat GÖNDERİLDİĞİNDE işler.
        //   · listPrice == salePrice geçerlidir. Doküman AYNI cümlede hem "aynı değer gönderebilirsiniz" hem
        //     "yüksek olmalıdır" diyor (kendi içinde çelişki); güvenli yorum listPrice >= salePrice — eşitlik
        //     indirimsiz ürünün normal hâlidir, yasaklarsak indirimsiz her ürünün senkronu durur.
        // Kanıt: doğrulama listeyi SIRAYLA gezer; hata ÜÇÜNCÜ satırdan gelirse ilk ikisi tüm guard'lardan geçmiştir.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[]
            {
                new N11RestPriceStock("STOK-ONLY", ListPrice: null, SalePrice: null, Quantity: 9, CurrencyType: null),
                new N11RestPriceStock("ESIT-FIYAT", ListPrice: 1600m, SalePrice: 1600m, Quantity: null, CurrencyType: "TL"),
                new N11RestPriceStock("TEL-TUZAGI", ListPrice: null, SalePrice: null, Quantity: 1, CurrencyType: "XAU"),
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:CurrencyTypeInvalid");
        ex.Data["StockCode"].ShouldBe("TEL-TUZAGI");
    }

    // ── 4) price-stock'un diğer belgelenmiş sınırları ──────────────────────────────────────────────

    [Fact]
    public async Task Row_that_changes_nothing_is_rejected()
    {
        // Yalnız stockCode taşıyan satır N11'de SESSİZ NO-OP olur: 200 döner, taskId üretilir, hiçbir şey değişmez.
        // Boşuna task açıp "senkronlandı" sanmak yerine yerelde duruyoruz.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[] { new N11RestPriceStock("ALT-KLY-14", null, null, null, null) },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:NothingToUpdate");
    }

    [Fact]
    public async Task Duplicate_stock_codes_in_one_request_are_rejected()
    {
        // N11 SKU'yu stockCode ile adresler: aynı istekte aynı kod iki kez geçerse hangi satırın kazandığı
        // BELİRSİZDİR. Sessiz veri kaybı yerine fail-fast — hangi satırın uygulandığını sonradan anlamak imkânsız.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[]
            {
                new N11RestPriceStock("ALT-KLY-14", null, null, 1, null),
                new N11RestPriceStock("ALT-KLY-14", null, null, 2, null),
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:DuplicateStockCode");
    }

    [Fact]
    public async Task Stock_code_longer_than_the_documented_limit_is_rejected()
    {
        // Doküman: stockCode "maksimum değeri 255". Uzun kod TÜM partiyi REJECT ettirir; tek satır yüzünden
        // 999 ürünün güncellemesi kaybolmasın diye sınır yerelde uygulanır.
        var tooLong = new string('X', N11RestConsts.MaxStockCodeLength + 1);

        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[] { new N11RestPriceStock(tooLong, null, null, 1, null) },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:StockCodeTooLong");
    }

    [Fact]
    public async Task Quantity_above_the_documented_ceiling_is_rejected()
    {
        // Doküman: quantity "maksimum değer 999.999". Aşan değer REJECT sebebidir; ayrıca bizde böyle bir stok
        // gerçekçi olmadığından hesaplama hatasının (ör. gram × adet karışması) erken alarmıdır.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdatePriceStockAsync(
            new[] { new N11RestPriceStock("ALT-KLY-14", null, null, N11RestConsts.MaxQuantity + 1, null) },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:QuantityOutOfRange");
    }

    [Fact]
    public async Task Empty_input_produces_no_request_at_all()
    {
        // Değişen ürün yoksa ağa ÇIKILMAZ: boş bir parti hem kotayı yer hem de takip edilecek anlamsız bir
        // taskId üretir. (Bu test gerçekten gönderim yoluna girer — boş liste erken döndüğü için ağ görülmez.)
        var submissions = await _client.UpdatePriceStockAsync(
            Array.Empty<N11RestPriceStock>(), FakeAppKey, FakeAppSecret);

        submissions.ShouldBeEmpty();
    }

    // ── 5) product-create guard'ları ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Vat_rate_outside_the_documented_set_is_rejected()
    {
        // Doküman KDV oranını 0/1/10/20 ile sınırlar. Kuyumda %8 alışkanlığı (eski oran) hâlâ elle giriliyor;
        // yanlış oran TÜM partiyi REJECT ettirir. İlk satır GEÇERLİ — hata ikinciden geldiği için baseline'ın
        // (tüm zorunlu alanlar + https görsel + attribute) gerçekten geçtiği de kanıtlanmış olur.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.CreateProductsAsync(
            new[]
            {
                ValidCreate("GECERLI-SATIR"),
                ValidCreate("KDV-8") with { VatRate = 8 },
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:VatRateInvalid");
        ex.Data["StockCode"].ShouldBe("KDV-8");
    }

    [Fact]
    public async Task Non_https_image_url_is_rejected()
    {
        // Doküman görsel URL'lerinin https olmasını şart koşar. http bağlantı N11 tarafında indirilemez ⇒ ürün
        // görselsiz açılır ya da REJECT alır; ikisi de sessiz kalitesizliktir. Bizim DAM URL'lerimiz https'tir,
        // ama pazaryerinden içe aktarılmış eski kayıtlarda http kalabilir — guard burada.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.CreateProductsAsync(
            new[]
            {
                ValidCreate("HTTP-GORSEL") with
                {
                    Images = new[] { new N11RestProductImage("http://cdn.example.com/kolye-1.jpg", 1) },
                },
            },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:ImageUrlNotHttps");
    }

    // ── 6) product-update: SESSİZ NO-OP tuzağı ─────────────────────────────────────────────────────

    [Fact]
    public async Task Product_main_id_update_without_the_delete_flag_is_rejected()
    {
        // BU TESTİN VARLIK SEBEBİ: N11 productMainId'yi YALNIZ deleteProductMainId=true iken günceller; bayrak
        // yokken HİÇBİR ŞEY yapmaz ve HATA DA DÖNMEZ. Bayrağın adı "sil" ama işlevi "değiştirmeye izin ver" —
        // sezgiye aykırı olduğu için kolayca atlanır. Sessiz no-op'u gürültülü hataya çeviriyoruz: varyant
        // gruplaması güncellendi sanıp N11'de eski grubun kalması, ürünlerin birleşmemesi demektir.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdateProductsAsync(
            new[] { new N11RestProductUpdate("ALT-KLY-14", ProductMainId: "GRUP-ALT-KLY") },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:ProductMainIdUpdateNeedsFlag");
    }

    [Fact]
    public async Task Update_row_with_no_changed_field_is_rejected()
    {
        // Yalnız stockCode gönderilen product-update de sessiz no-op'tur (200 + taskId + hiçbir değişiklik).
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdateProductsAsync(
            new[] { new N11RestProductUpdate("ALT-KLY-14") },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:NothingToUpdate");
    }

    [Fact]
    public async Task Product_status_outside_the_documented_set_is_rejected()
    {
        // Ürünü satıştan çekmenin resmî REST yolu status="Suspended"tir; "Passive"/"Inactive" gibi uydurma bir
        // değer REJECT alır ve ürün SATIŞTA KALIR — yani stoğu bitmiş ürün satılmaya devam eder.
        var ex = await Should.ThrowAsync<BusinessException>(() => _client.UpdateProductsAsync(
            new[] { new N11RestProductUpdate("ALT-KLY-14", Status: "Passive") },
            FakeAppKey, FakeAppSecret));

        ex.Code.ShouldBe("TradeXpress:N11:Rest:ProductStatusInvalid");
    }

    // ── 7) Fiyat biçimi: NOKTA ayraç, TAM 2 hane ───────────────────────────────────────────────────

    [Fact]
    public void Prices_use_a_dot_and_exactly_two_decimals_even_under_turkish_culture()
    {
        // BU TESTİN VARLIK SEBEBİ (iki ayrı REJECT sebebi tek testte):
        //   1) "küsurat bilgisi nokta ile ayrılmalıdır. Virgül kullanımı hata alınmasına sebebiyet verecektir."
        //   2) "küsurat noktadan sonra 2 hane iletilmelidir. Aksi takdirde isteğiniz REJECT alacaktır."
        // Uygulama tr-TR kültüründe koşuyor; kültüre duyarlı tek bir ToString() çağrısı "1234,50" üretir ve TÜM
        // parti reddedilir. Ayrıca 1234.5m ondalık ÖLÇEĞİ 1'dir — biçimlenmezse "1234.5" yazılır, o da REJECT.
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            // Önce testin ÖNCÜLÜ doğrulanır: kültür gerçekten devrede mi? Kültüre duyarlı biçimlendirme VİRGÜL
            // üretmiyorsa test sessizce boşa geçerdi (invariant ortamda hiçbir şey kanıtlamazdı).
            var cultureSensitive = 1234.5m;
            cultureSensitive.ToString("0.00").ShouldBe("1234,50");

            N11RestPrice.Format(1234.5m).ShouldBe("1234.50");   // ölçek 1 → 2'ye tamamlanır
            N11RestPrice.Format(999m).ShouldBe("999.00");       // ölçek 0 → 2'ye tamamlanır
            N11RestPrice.Format(0.1m).ShouldBe("0.10");         // baştaki sıfır korunur (".10" değil)

            // Aynı kural JSON body'sinde de geçerli olmalı: converter fiyatı SAYI token'ı olarak ama tam 2 haneyle
            // yazar (WriteNumberValue sondaki sıfırı düşürürdü → "1600" → REJECT).
            var json = SerializePriceStockBody(
                new N11RestPriceStock("A", ListPrice: 1234.5m, SalePrice: 999m, Quantity: null, CurrencyType: "TL"),
                new N11RestPriceStock("B", ListPrice: 0.1m, SalePrice: 0.1m, Quantity: null, CurrencyType: "TL"));

            var skus = Skus(json);
            PriceText(skus[0], "listPrice").ShouldBe("1234.50");
            PriceText(skus[0], "salePrice").ShouldBe("999.00");
            PriceText(skus[1], "listPrice").ShouldBe("0.10");
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void More_than_two_decimals_are_rounded_to_what_n11_can_represent()
    {
        // Gram-altın fiyatlaması rutin olarak 2'den fazla ondalık üretir (kur × gram × milyem). N11 2 haneden
        // fazlasını TEMSİL EDEMEZ; olduğu gibi göndermek garantili REJECT'tir. Bu yüzden taşıma katmanı yuvarlar —
        // sessiz bir kayıp değil, tek temsil edilebilir değere indirgemedir. Orta-nokta (x.xx5) KASTEN test
        // edilmiyor: yuvarlama modu iş kararıdır, taşıma sözleşmesi değil.
        var json = SerializePriceStockBody(
            new N11RestPriceStock("A", ListPrice: 1234.567m, SalePrice: 1234.561m, Quantity: null, CurrencyType: "TL"));

        var sku = Skus(json).Single();
        PriceText(sku, "listPrice").ShouldBe("1234.57");
        PriceText(sku, "salePrice").ShouldBe("1234.56");
    }

    // ── 8) 1000 SKU parti sınırı ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Rows_are_split_into_batches_of_at_most_a_thousand_skus()
    {
        // Doküman: "Tek seferde maximum 1000 sku için güncelleme atabilirsiniz." Aşıldığında N11 fazlasını
        // SESSİZCE kırpabilir — yani 2500 SKU gönderip 200 OK alırsak 1500 ürünün fiyatı bayat kalır ve bunu
        // hiçbir yerde göremeyiz. Dilimleme istemcinin sorumluluğudur; her dilim AYRI bir taskId üretir.
        // İstemcinin kullandığı mekanizmanın AYNISI (LINQ Chunk + aynı sabit) burada doğrulanır: sabit değişirse
        // ya da Chunk semantiği yanlış varsayılırsa bu test kırmızıya döner.
        N11RestConsts.MaxSkusPerRequest.ShouldBe(1000);

        var stockCodes = Enumerable.Range(0, 2500).Select(i => $"SKU-{i}").ToList();
        var batches = stockCodes.Chunk(N11RestConsts.MaxSkusPerRequest).ToList();

        batches.Count.ShouldBe(3);
        batches.Select(b => b.Length).ShouldBe(new[] { 1000, 1000, 500 });

        // Dilimleme kayıpsız ve sırayı bozmadan olmalı: düşen bir SKU sessizce senkronsuz kalırdı.
        batches.SelectMany(b => b).ShouldBe(stockCodes);
    }

    [Fact]
    public void Exactly_one_thousand_rows_stay_in_a_single_batch()
    {
        // Off-by-one koruması: sınır DAHİLDİR. ">= 1000" mantığıyla bölünseydi 1000 kalem 2 isteğe düşer,
        // ikincisi BOŞ bir parti olurdu — N11 boş skus dizisini reddeder.
        Enumerable.Range(0, N11RestConsts.MaxSkusPerRequest)
            .Chunk(N11RestConsts.MaxSkusPerRequest)
            .Count()
            .ShouldBe(1);
    }

    // ── 9) Asenkron task statüsü (SSOT: N11TaskStates) ─────────────────────────────────────────────

    [Fact]
    public void Documented_task_statuses_map_to_their_states()
    {
        // Doküman üç statü belgeliyor: PROCESSED = tamamlandı · IN_QUEUE = işleniyor · REJECT = hiç işlenmedi.
        // Yorum tek yerde (SSOT) yapılır ki ham dizgiler koda dağılmasın; hem yazma makbuzu hem poller aynı
        // sözlüğü kullanır. "REJECTED" yazımı savunma amaçlı karşılanır (N11 alan yazımlarını değiştirebiliyor).
        N11TaskStates.Parse("PROCESSED").ShouldBe(N11TaskState.Processed);
        N11TaskStates.Parse("IN_QUEUE").ShouldBe(N11TaskState.InQueue);
        N11TaskStates.Parse("REJECT").ShouldBe(N11TaskState.Rejected);
        N11TaskStates.Parse("REJECTED").ShouldBe(N11TaskState.Rejected);
    }

    [Fact]
    public void Task_status_matching_ignores_case_and_surrounding_space()
    {
        // Dokümanın kendi metni statüleri karışık yazıyor ve N11 bazı statü alanlarını baştaki boşlukla döndürüyor.
        // Duyarlı bir karşılaştırma, N11 yazımı değiştirdiği gün TÜM task'ları "bilinmeyen" sayardı.
        N11TaskStates.Parse("processed").ShouldBe(N11TaskState.Processed);
        N11TaskStates.Parse(" IN_QUEUE ").ShouldBe(N11TaskState.InQueue);
    }

    [Fact]
    public void Unrecognized_or_missing_task_status_maps_to_unknown()
    {
        // Belgelenmemiş bir dördüncü statüyü Processed'a çevirmek satırları yanlışlıkla "senkronlandı" yapar,
        // Rejected'a çevirmek gereksiz alarm üretir. Dört değerli eşleme (+ log) doğrudur.
        N11TaskStates.Parse("SOMETHING_NEW").ShouldBe(N11TaskState.Unknown);
        N11TaskStates.Parse(null).ShouldBe(N11TaskState.Unknown);
        N11TaskStates.Parse("   ").ShouldBe(N11TaskState.Unknown);
    }

    [Fact]
    public void Only_the_success_text_marks_a_sku_as_synchronised()
    {
        // FAIL-CLOSED: başarı YALNIZ "SUCCESS" metninden türetilir, "Fail değilse başarılıdır" mantığından DEĞİL.
        // Dokümanın cümlesi bile karışık yazıyor ("Fail ve SUCCESS") ⇒ karşılaştırma harf-duyarsız. N11 yarın ara
        // bir statü (ör. PARTIAL) eklerse, tersi mantık o satırları senkronlanmış sayar ve fiyat farkı sessizce
        // kalıcılaşır — kısmi başarı bu uçta NORMALDİR, o yüzden satır bazlı okuma şarttır.
        N11TaskStates.IsItemSuccess("SUCCESS").ShouldBeTrue();
        N11TaskStates.IsItemSuccess("Success").ShouldBeTrue();
        N11TaskStates.IsItemSuccess(" SUCCESS ").ShouldBeTrue();

        N11TaskStates.IsItemSuccess("Fail").ShouldBeFalse();
        N11TaskStates.IsItemSuccess("FAIL").ShouldBeFalse();
        N11TaskStates.IsItemSuccess("PARTIAL").ShouldBeFalse();
        N11TaskStates.IsItemSuccess(null).ShouldBeFalse();
    }

    // ── 10) product-query: sayfa boyutu tavanı ve filtre kurulumu ──────────────────────────────────

    [Fact]
    public void Page_size_is_clamped_to_the_documented_maximum()
    {
        // Resmî REST dokümanı (2026-02-04, satır 1010): "size Varsayılan 20 maksimum 250".
        // Tavanı aşan istekte N11 ya hata döner ya da SESSİZCE kendi sınırını uygular; ikinci hâlde import
        // döngüsü istediği adımla ilerlediğini sanıp ürünlerin bir kısmını ATLAR — sınırı biz uyguluyoruz.
        //
        // NOT: bu test bir zamanlar 50'yi kilitliyordu; o değer v9.0 SOAP dokümanından geliyordu ve REST için
        // BAYATTI (2026-08-03 düzeltmesi). Sayı değişti çünkü KAYNAK değişti, kural gevşemedi.
        var query = Query(N11ProductQueryClient.BuildUrl(
            new N11ProductQueryFilter(Page: 0, Size: 1000, StockCode: null, SaleStatus: null,
                ProductStatus: null, BrandName: null, CategoryIds: null), QueryBase));

        query["size"].ShouldBe("250");
    }

    [Fact]
    public void Page_size_below_the_maximum_is_left_alone()
    {
        // Karşı taraf: kırpma "hep 250'ye çek" DEĞİL — istenen değer tavanın altındaysa aynen geçer.
        var query = Query(N11ProductQueryClient.BuildUrl(
            new N11ProductQueryFilter(Page: 0, Size: 100, StockCode: null, SaleStatus: null,
                ProductStatus: null, BrandName: null, CategoryIds: null), QueryBase));

        query["size"].ShouldBe("100");
    }

    [Fact]
    public void Non_positive_size_and_negative_page_fall_back_to_documented_defaults()
    {
        // Fail-safe: "size=0" satıcının TÜM ürünlerini boş sayfalarla taramaya çevirir, "page=-1" ise 4xx üretir.
        // Dokümanın kendi varsayılanları (page 0, size 20) tek doğru geri düşüştür.
        var query = Query(N11ProductQueryClient.BuildUrl(
            new N11ProductQueryFilter(Page: -3, Size: 0, StockCode: null, SaleStatus: null,
                ProductStatus: null, BrandName: null, CategoryIds: null), QueryBase));

        query["page"].ShouldBe("0");
        query["size"].ShouldBe("20");
    }

    [Fact]
    public void Unset_filters_are_left_out_of_the_query_string()
    {
        // Dokümanın örnek isteği boş parametreler gönderiyor (stockCode=&brandName=…), ama boş bir değer
        // "bu alan boş STRING olsun" diye yorumlanabilir ve 0 kayıt döndürür. Yazma uçlarındaki
        // "gönderilmeyen alan güncellenmez" ilkesiyle aynı hijyen: bilinmeyen alan HİÇ yazılmaz.
        var url = N11ProductQueryClient.BuildUrl(
            new N11ProductQueryFilter(Page: 0, Size: 20, StockCode: null, SaleStatus: null,
                ProductStatus: null, BrandName: null, CategoryIds: null), QueryBase);

        url.ShouldStartWith("https://api.n11.com/ms/product-query?");

        var query = Query(url);
        query.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(new[] { "page", "size" });
    }

    [Fact]
    public void Filter_values_are_url_encoded()
    {
        // Marka/stok kodunda boşluk, & ve Türkçe karakter gerçek veridir ("Altın & Gümüş"). Ham yazılırsa & bir
        // sonraki parametreyi başlatır ve filtre SESSİZCE başka bir şeye dönüşür (yanlış ürün kümesi içe aktarılır).
        var url = N11ProductQueryClient.BuildUrl(
            new N11ProductQueryFilter(Page: 2, Size: 50, StockCode: "ALT KLY&14", SaleStatus: "On_Sale",
                ProductStatus: "Active", BrandName: "Altın & Gümüş", CategoryIds: "1001,1002"), QueryBase);

        url.ShouldNotContain("&14");                     // ham & parametreyi bölerdi

        var query = Query(url);
        query["page"].ShouldBe("2");
        query["stockCode"].ShouldBe("ALT KLY&14");       // çözüldüğünde birebir geri gelmeli
        query["brandName"].ShouldBe("Altın & Gümüş");
        query["saleStatus"].ShouldBe("On_Sale");
        query["productStatus"].ShouldBe("Active");
        query["categoryIds"].ShouldBe("1001,1002");
    }

    [Fact]
    public void Product_summary_holds_an_id_that_int_cannot_represent()
    {
        // Doküman birebir uyarıyor: "n11ProductId alanı 9 haneden 10 haneye çıkabilir". Alan int'e daraltılırsa
        // BU DOSYA DERLENMEZ (11 haneli literal int'e sığmaz) — yani koruma çalışma zamanına kalmadan, alanı
        // daraltan commit'in kendisinde patlar. Daralma N11 tarafındaki bir genişlemede tüm sayfayı düşürürdü.
        var summary = new N11RestProductSummary(
            N11ProductId: 12_345_678_901L,
            ProductMainId: "GRUP-ALT-KLY",
            StockCode: "ALT-KLY-14",
            Title: "Altın Kolye 14 Ayar",
            SalePrice: 10000m,
            ListPrice: 12000m,
            Quantity: 2,
            SaleStatus: "On_Sale",
            ProductStatus: "Active",
            CategoryId: "1231231",
            ImageUrls: new[] { "https://n11scdn.akamaized.net/a1/org/IMG-1.jpg" });

        summary.N11ProductId.ShouldBe(12_345_678_901L);
    }

    /// <summary>Yanıt GÖRSEL TAŞIR — modelde bir zamanlar "yanıt görsel taşımaz" yazıyordu ve bu v9.0 SOAP
    /// dokümanından gelen BAYAT bir tespitti. Resmî REST dokümanı (2026-02-04) <c>imageUrls</c>'i hem alan
    /// tablosunda hem örnek yanıtta veriyor; mağaza içe aktarımı görselleri buradan alıp DAM'a indirebilir.
    /// Alan modelden düşerse bu test DERLENMEZ.</summary>
    [Fact]
    public void Product_summary_carries_image_urls_for_dam_import()
    {
        var summary = new N11RestProductSummary(
            N11ProductId: 1L,
            ProductMainId: null,
            StockCode: "ALT-KLY-14",
            Title: null,
            SalePrice: null,
            ListPrice: null,
            Quantity: null,
            SaleStatus: null,
            ProductStatus: null,
            CategoryId: null,
            ImageUrls: new[] { "https://n11scdn.akamaized.net/a1/org/IMG-1.jpg", "https://n11scdn.akamaized.net/a1/org/IMG-2.jpg" });

        summary.ImageUrls.Count.ShouldBe(2);
    }

    // ── Yardımcılar ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// price-stock body'sini istemcinin gönderim yolundaki ADIMLARLA aynı şekilde kurar: dokümanın zarfı
    /// (<c>payload.integrator</c> + <c>payload.skus</c>) + ortak serileştirme sözleşmesi.
    /// </summary>
    private static string SerializePriceStockBody(params N11RestPriceStock[] rows)
    {
        var envelope = new N11RestEnvelope<N11RestPriceStock>(
            new N11RestPayload<N11RestPriceStock>(N11RestConsts.Integrator, rows));

        return JsonSerializer.Serialize(envelope, WriteJson);
    }

    /// <summary>Dokümanın tüm zorunlu alanlarını taşıyan GEÇERLİ bir ürün satırı — mutasyon testlerinin taban çizgisi.</summary>
    private static N11RestProductCreate ValidCreate(string stockCode)
    {
        return new N11RestProductCreate(
            Title: "Altın Kolye 14 Ayar",
            Description: "14 ayar altın kolye, 40 cm zincir.",
            CategoryId: 1231231,
            CurrencyType: "TL",
            ProductMainId: "GRUP-ALT-KLY",
            PreparingDay: 3,
            ShipmentTemplate: "Standart Kargo",
            StockCode: stockCode,
            Quantity: 5,
            Images: new[] { new N11RestProductImage("https://cdn.example.com/kolye-1.jpg", 1) },
            Attributes: new[] { new N11RestProductAttribute(1, ValueId: 42, CustomValue: null) },
            SalePrice: 1100m,
            ListPrice: 1200m,
            VatRate: 20);
    }

    private static IReadOnlyList<JsonElement> Skus(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement
            .GetProperty("payload")
            .GetProperty("skus")
            .EnumerateArray()
            .Select(e => e.Clone())        // JsonDocument dispose edilince ham tampon geçersizleşir
            .ToList();
    }

    /// <summary>
    /// Fiyatın JSON'daki METNİ. Sayı mı yoksa string mi yazıldığı taşıma kararıdır ve kasten kilitlenmemiştir;
    /// kilitlenen şey RAKAMLARIN kendisidir (nokta ayraç + tam 2 hane) — testi kırılgan yapmadan.
    /// </summary>
    private static string PriceText(JsonElement sku, string propertyName)
    {
        return sku.GetProperty(propertyName).GetRawText().Trim('"');
    }

    private static Dictionary<string, string> Query(string url)
    {
        var separator = url.IndexOf('?');
        var queryString = separator < 0 ? string.Empty : url[(separator + 1)..];

        return queryString
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                pair => pair[0],
                // '+' kodlaması da kabul edilir: EscapeDataString %20, UrlEncode '+' üretir — ikisi de geçerli.
                pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1].Replace("+", "%20") : string.Empty),
                StringComparer.Ordinal);
    }
}
