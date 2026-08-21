using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Conventions;

/// <summary>
/// SATIŞA HAZIRLIK PANELİ ⇄ PUSH ENGELLERİ PARİTESİ (2026-08-21 ölçümü).
///
/// <para><b>Neden var:</b> "bu ürün neden satışta değil?" sorusunun iki ayrı cevabı oluştu ve ikisi birbirini
/// bilmiyor. Biri <c>ProductSaleValidator</c> — panelde issue listesi üretir, sekmeleri boyar, kullanıcı push'a
/// basmadan ÖNCE okur. Diğeri push/sync yolundaki <c>BusinessException</c>'lar — kullanıcı push'a bastıktan SONRA,
/// tek bir hata kutusu olarak görünür. Ölçüm: push yolunda fırlatılan kuralların bir kısmının panelde HİÇBİR
/// karşılığı yok; yani panel yeşilken push düşüyor. En pahalı hata sınıfı bu — kullanıcı ürünü hazır sanıp
/// bekliyor, engel ancak gönderim anında (bazen hiç uyarı vermeden) ortaya çıkıyor.</para>
///
/// <para><b>Bu test ne yapar:</b> iki kod kümesini KAYNAKTAN çıkarır — panel issue kodları
/// (<c>ProductSaleValidator</c> sabitleri) ve push yolundaki exception kodları (N11/Trendyol app service +
/// <c>N11ProductPushValidator</c>/<c>TrendyolProductPushValidator</c> + <c>ChannelPushGuard</c>). Sonra her push
/// kodunun ya bir panel karşılığı olmasını (<see cref="PanelCounterparts"/>) ya da GEREKÇELİ bir allow-list
/// satırı taşımasını (<see cref="AllowList"/>) şart koşar. Yeni bir push engeli eklendiğinde test KIRMIZI olur:
/// ya panele bir dal eklenir, ya da neden eklenmediği buraya yazılır. Karar kaydedilmeden geçilemez.</para>
///
/// <para><b>Test BUGÜN YEŞİLDİR</b> — mevcut boşluklar <c>PanelGapReason</c> gerekçesiyle allow-list'tedir.
/// <b>Allow-list böyle erir:</b> bir push kuralının panelde karşılığı açıldığında satır allow-list'ten
/// <see cref="PanelCounterparts"/>'a TAŞINIR; <c>PanelGapReason</c> taşıyan satır sayısı bu borcun ölçüsüdür ve
/// <see cref="Panel_gap_debt_is_counted_and_visible"/> onu her koşuda yazdırır. Sıfıra indiğinde borç kapanmıştır.
/// Diğer gerekçeler (kayıt anı doğrulaması · altyapı · gönderim anı · push satırı kurulumu · teşhis ucu)
/// KALICIDIR, erimeleri beklenmez.</para>
///
/// <para><b>Bayat regex koruması:</b> bu kod tabanında hiçbir şey bulamayınca sessizce yeşil kalan convention
/// testleri yaşandı. Burada her kaynak dosya için "en az bir kod çıkmalı" şartı var, panel tarafında bir taban
/// sayı var, ve mapping/allow-list'te ARTIK VAR OLMAYAN bir koda satır kalması da KIRMIZI — yani kod yeniden
/// adlandırıldığında bu dosya sessizce bayatlayamaz.</para>
/// </summary>
public class SaleReadinessPushParityTests
{
    // ── Kaynaklar ─────────────────────────────────────────────────────────────────────────────────────

    private const string ValidatorPath = "src/Integration.TradeXpress.Application/Products/ProductSaleValidator.cs";

    /// <summary>Push/sync yolunda kullanıcıyı DURDURAN kod ne buralarda yaşar. Liste elle tutulur ama
    /// <see cref="Every_push_validator_in_the_tree_is_covered_by_this_net"/> ağaçta yeni bir
    /// <c>*PushValidator.cs</c> belirdiğinde KIRMIZI yakar — sessiz kapsam kaybı olmasın.</summary>
    private static readonly string[] PushPathPaths =
    {
        "src/Integration.TradeXpress.Application/N11Products/SalesChannelTrN11ProductAppService.cs",
        "src/Integration.TradeXpress.Application/N11Products/N11ProductPushValidator.cs",

        // Red sınıflandırması (PushRejected · PriceOutOfBand) 2026-08-21'de ortak statike taşındı (adet-0
        // dilimi) ve adet-0 gönderimi kendi yolunu açtı — literaller artık bu iki dosyada yaşıyor; taşınma
        // anında ölü-satır kontrolü haklı olarak kırmızı yandı.
        "src/Integration.TradeXpress.Application/N11Products/Rest/N11RestPushFailure.cs",
        "src/Integration.TradeXpress.Application/N11Products/N11StockWithdrawer.cs",

        "src/Integration.TradeXpress.Application/TrendyolProducts/SalesChannelTrTrendyolProductAppService.cs",
        "src/Integration.TradeXpress.Application/TrendyolProducts/TrendyolProductPushValidator.cs",
        "src/Integration.TradeXpress.Domain/SalesChannels/ChannelPushGuard.cs",
    };

    // Panel issue kodu sabiti: `public const string VariantNoSalePrice = "Variant:NoSalePrice";`
    // Adım anahtarları (`= "Category";`) iki nokta taşımadığı için bu desene TAKILMAZ — issue kodu ile
    // adım anahtarı karışırsa mapping anlamsız bir şeye bağlanmış olurdu.
    private static readonly Regex PanelIssueCodeRegex =
        new(@"public\s+const\s+string\s+\w+\s*=\s*""([A-Za-z]+:[A-Za-z]+)""\s*;", RegexOptions.Compiled);

    // Push exception kodu: "TradeXpress:<Kanal>:<Kapsam>:<Ad>". `new BusinessException(...)` yerine LİTERAL
    // taranır — Trendyol tarafında kod bir ternary'den geliyor (`NoVerifiedVariant`/`NoPricedVariant`) ve
    // ctor'a bağlı bir desen o iki kuralı SESSİZCE atlardı. Aynı kod L[...] ile mesaj olarak da okunuyor;
    // küme olduğu için tekrar zararsız.
    private static readonly Regex PushExceptionCodeRegex =
        new(@"""(TradeXpress:[A-Za-z0-9]+(?::[A-Za-z0-9]+)+)""", RegexOptions.Compiled);

    // ── Gerekçe etiketleri ────────────────────────────────────────────────────────────────────────────

    /// <summary>ERİYECEK BORÇ: push'u fiilen durduran ama panelde karşılığı olmayan kural (2026-08-21 ölçümü).
    /// Panele dal eklendiğinde satır <see cref="PanelCounterparts"/>'a taşınır.</summary>
    private const string PanelGapReason = "HENÜZ PANELDE YOK — 2026-08-21 ölçümü, kapatılacak";

    /// <summary>KALICI: kural push anında değil KAYIT anında çalışır (alan doğrulaması). Kullanıcı zaten formda
    /// anında geri bildirim alıyor; panelde ikinci kez göstermek "düzeltilemez uyarı" üretirdi.</summary>
    private const string SaveTimeReason = "KAYIT anı alan doğrulaması — push engeli değil, formda anında görünür";

    /// <summary>KALICI: kayıt/kanal/ürün bulunamadı, şirket yok gibi altyapı hatası. Bir iş kuralı değil, çağrının
    /// ön koşulu; panelde gösterilecek bir "eksik" karşılığı yok (kayıt yoksa panel de açılmaz).</summary>
    private const string PlumbingReason = "altyapı ön koşulu (kayıt/kanal/ürün/şirket yok) — iş kuralı değil";

    /// <summary>KALICI: sonucu ancak gönderim ANINDA belli olur (dış ağ, karşı tarafın cevabı). Panel push'tan
    /// önce okunduğu için önceden bilemez; kanalın cevabı zaten LastError'a yazılıp Channel:LastError olarak görünür.</summary>
    private const string RuntimeReason = "yalnız gönderim anında bilinebilir (dış ağ / karşı tarafın cevabı)";

    /// <summary>KALICI: kural push-SATIRI kurulumunu ister — kanal SKU/kombinasyon kaydı + türetilmiş fiyat zinciri
    /// (Override ?? türetilmiş ?? varyant) koşulmadan sonucu bilinemez. Panel push'tan ÖNCE, ürün snapshot'ından
    /// okur; aynı kararı vermesi için push zincirinin kendisini koşması gerekirdi (ucuz karşılık yok). Varyant
    /// <c>SalePrice</c>'a bakan bir yaklaşıklık da kurulmaz: fiyat zinciri SalePrice'ı ezebildiğinden hem yanlış
    /// alarm hem kaçak üretirdi (2026-08-21 kararı — MixedCurrency panele alınırken bu üçü bilinçle alınmadı).</summary>
    private const string PushRowReason =
        "push satırı kurulumunu ister (kanal SKU/kombinasyon kaydı + türetilmiş fiyat) — panel ürün snapshot'ından ucuz üretemez";

    /// <summary>KALICI: kullanıcının BİLEREK tıkladığı teşhis ucu ("durumu sorgula", "kuyruğu çöz"). Sorgulanacak
    /// bir şey olmaması satışa hazırlık eksikliği değil, o an yapacak iş olmamasıdır.</summary>
    private const string DiagnosticReason = "teşhis ucu — sorgulanacak iş yokluğu, satışa hazırlık eksikliği değil";

    // ── Panel karşılığı OLAN push kodları ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Push kodu → onu ÖNCEDEN görünür kılan panel issue kodu. Değer <c>ProductSaleValidator</c>'da GERÇEKTEN
    /// var olmalıdır (<see cref="Mapped_panel_codes_must_exist_in_the_validator"/>) — panel kodu yeniden
    /// adlandırılırsa bu dosya sessizce yalan söyleyemez.
    /// </summary>
    private static readonly Dictionary<string, string> PanelCounterparts = new(StringComparer.Ordinal)
    {
        // ── N11 ──
        // Görselsiz ürün: panelde Warning (Product:NoImage) + board'da CanPush=false ile düğme kapalı.
        ["TradeXpress:N11:Product:ImagesRequired"] = "Product:NoImage",

        // Fiyatsız varyant. KISMİ: panel ERP varyantının SalePrice'ına bakar; push satırı türetilmiş fiyatı da
        // çözemeyince düşer (kur yok / marj yok) — o hâlin panelde ayrı bir issue'su yok.
        ["TradeXpress:N11:Product:NoPricedVariant"] = "Variant:NoSalePrice",
        ["TradeXpress:N11:StockItem:PriceMissingForPush"] = "Variant:NoSalePrice",

        // "Önce push, sonra sync" ön koşulu = panelde Channel:NotPushed (Info).
        ["TradeXpress:N11:Product:NotPushedYet"] = "Channel:NotPushed",

        // Çözülmemiş task varken yeni push yazılmaz — panelde Channel:Pending (builder: PendingPushTaskId != null).
        ["TradeXpress:N11:Rest:PushPending"] = "Channel:Pending",

        // Kanalın REDDİ. Panel bunu ancak DENEMEDEN SONRA gösterir: FriendlyError → MarkSyncFailed/LastError →
        // Channel:LastError. Önceden uyarı değil, sonradan görünürlük — ama karşılığı vardır ve kaybolmaz.
        ["TradeXpress:N11:Rest:PushRejected"] = "Channel:LastError",
        ["TradeXpress:N11:Rest:PriceOutOfBand"] = "Channel:LastError",

        // Pasif kanal kaydına senkron yazılmaz (Trendyol'daki ArchivedNoSync'in N11 karşılığı, 2026-08-21'de
        // eklendi). Builder N11 kolunda da IsActive'i taşıdığı için panel aynı bilgiyi Channel:Passive olarak
        // gösteriyor — bu satır o eşlemenin mekanik kaydıdır.
        ["TradeXpress:N11:Product:PassiveNoSync"] = "Channel:Passive",
        // Pasif kayda ELLE tam push reddi (2026-08-21) — panel karşılığı aynı: satır zaten Channel:Passive uyarısı taşır.
        ["TradeXpress:N11:Product:PassiveNoPush"] = "Channel:Passive",
        ["TradeXpress:Trendyol:Product:ArchivedNoPush"] = "Channel:Passive",

        // ── Trendyol ──
        // Kategori/marka eksikliği: builder MissingRequiredFields'ı (CategoryId ya da BrandId boş) Error yapar.
        ["TradeXpress:Trendyol:Product:CategoryRequired"] = "Channel:MissingRequiredFields",

        ["TradeXpress:Trendyol:Product:ImagesRequired"] = "Product:NoImage",
        ["TradeXpress:Trendyol:Product:NoPricedVariant"] = "Variant:NoSalePrice",
        ["TradeXpress:Trendyol:Product:PriceMissingForPush"] = "Variant:NoSalePrice",

        // Fiyatlı ama doğrulanmamış varyant — panelde Variant:NotVerified / Variant:VerificationStale.
        ["TradeXpress:Trendyol:Product:NoVerifiedVariant"] = "Variant:NotVerified",

        ["TradeXpress:Trendyol:Product:NotPushedYet"] = "Channel:NotPushed",

        // Devam eden batch: builder IsPending = BatchRequestId != null && Status == PROCESSING → Channel:Pending.
        ["TradeXpress:Trendyol:Product:BatchInProgress"] = "Channel:Pending",

        // Arşivdeki listeleme. Kanal-ürünün IsActive'i Trendyol arşiv durumunun projeksiyonu olduğu için
        // panel aynı bilgiyi Channel:Passive olarak zaten gösteriyor (Channel:Obstacle de aynı kaydı işaretler).
        ["TradeXpress:Trendyol:Product:ArchivedNoSync"] = "Channel:Passive",

        // KARIŞIK PARA BİRİMİ (2026-08-21'de panele alındı — Product:MixedCurrency, Warning). Birim kaynağı
        // push satırıyla AYNI alan (ProductVariantDetail.SalePriceCurrencyUnitId). KISMİ asimetri bilinçli:
        // Trendyol her karışımda keser (birebir); N11 kanal/ürün birimi SEÇİLİYKEN muaftır ama panel kanal
        // birimini görmediği için yine uyarır — Warning düzeyinde fazla-uyarı, sessiz kaçaktan iyidir.
        ["TradeXpress:N11:Product:MixedCurrency"] = "Product:MixedCurrency",
        ["TradeXpress:Trendyol:Product:MixedCurrency"] = "Product:MixedCurrency",

        // ⚠ AĞIRLIK ASİMETRİSİ — BİLİNÇLİ, DÜZELTİLMEYECEK: panelde Product:VatMissing **Info**'dur (KDV'yi engel
        // hâline getiren kural EKLENMEZ — Hakan 2026-08-20), Trendyol push'u ise KDV'siz ürünü SERT reddeder.
        // Yani karşılık VARDIR ama panel bunu "engel" diye boyamaz. Kullanıcı Trendyol'da tıkanırsa sebebi
        // panelde Info satırı olarak durur; ağırlığı yükseltmek yasak, o yüzden burası bir borç DEĞİL.
        ["TradeXpress:Trendyol:Product:VatRateRequired"] = "Product:VatMissing",
    };

    // ── Panel karşılığı OLMAYAN push kodları (gerekçeli) ──────────────────────────────────────────────

    /// <summary>
    /// Her satır bir KARAR kaydıdır: "bu push engelinin panelde karşılığı neden yok". <see cref="PanelGapReason"/>
    /// taşıyan satırlar ERİYECEK borçtur; diğerleri kalıcı gerekçedir. Satır eklemek = kararı yazmak; gerekçesiz
    /// satır bu listede yer alamaz (derleyici zorlar — Dictionary değeri zorunlu).
    /// </summary>
    private static readonly Dictionary<string, string> AllowList = new(StringComparer.Ordinal)
    {
        // ══ ERİYECEK BORÇ: push'u durduruyor, panel sessiz ══════════════════════════════════════════════
        //
        // Nitelik/varyant ekseni ailesi (N11 + Trendyol push validator'ları). Kanal kategorisinin izin verdiği
        // eksenler ile ürünün varyant eksenleri uyuşmuyorsa push düşer; panel bugün yalnız "kategori seçilmiş mi"
        // diye bakıyor (Channel:MissingRequiredFields), eksen UYUMUNA hiç bakmıyor.
        ["TradeXpress:N11:Product:CategoryHasNoVariantAxis"] = PanelGapReason,
        ["TradeXpress:N11:Product:VariantAxisNotAllowed"] = PanelGapReason,
        ["TradeXpress:N11:Product:VariantAxisMissing"] = PanelGapReason,
        ["TradeXpress:N11:Product:VariantAttributesInconsistent"] = PanelGapReason,
        ["TradeXpress:N11:Product:DuplicateVariantSignature"] = PanelGapReason,
        ["TradeXpress:N11:Product:ProductAttributeMissing"] = PanelGapReason,
        ["TradeXpress:N11:Product:AttributeValueNotInList"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:CategoryHasNoVariantAxis"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:VariantAxisNotAllowed"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:VariantAxisMissing"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:VariantAttributesInconsistent"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:DuplicateVariantSignature"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:ProductAttributeMissing"] = PanelGapReason,
        ["TradeXpress:Trendyol:Product:AttributeValueNotInList"] = PanelGapReason,

        // ══ KALICI: push satırı kurulumu ister (2026-08-21 kararı — PanelGapReason'dan buraya taşındı) ═══
        //
        // Gönderilecek satır kalmadı (senkron kolu): kanal SKU defteri ile materialize edilen satırların
        // kesişimi ancak push zinciri koşularak bilinir. Çevresi panelde kapalı: hiç SKU yoksa Channel:NotPushed,
        // 0 kurulabilir varyant hâli adet-0 dalına gider (NoSyncableSku'ya hiç düşmez).
        ["TradeXpress:N11:Product:NoSyncableSku"] = PushRowReason,

        // Mükerrer stok kodu: çakışma ERP varyant kodu × N11-only KOMBİNASYON-türevli kod arasında —
        // kombinasyonlar ve sıra numarası soneki kanal kaydında yaşar, panel ürün snapshot'ında yoktur.
        ["TradeXpress:N11:Product:DuplicateStockCode"] = PushRowReason,

        // FİYAT BANDI: bant kanal kaydında, denetlenen fiyat push ANINDA türetilir (repricing zinciri).
        // SalePrice'ı banda vurmak yanlış alarm/kaçak üretirdi. Görünürlük sonradan gelir: ihlal MarkSyncFailed
        // ile LastError'a düşer → panel Channel:LastError gösterir (deftere satır YAZILMAZ — guard, satır daha
        // kurulmadan fırlar ve "denenmemişi yazma" kuralı uydurmaya izin vermez).
        ["TradeXpress:SalesChannel:Product:PriceOutOfBand"] = PushRowReason,

        // ══ KALICI: kayıt anı alan doğrulaması ═════════════════════════════════════════════════════════
        ["TradeXpress:SalesChannel:Product:SafetyStockNegative"] = SaveTimeReason,
        ["TradeXpress:SalesChannel:Product:PriceBandNegative"] = SaveTimeReason,
        ["TradeXpress:SalesChannel:Product:PriceBandInverted"] = SaveTimeReason,
        ["TradeXpress:N11:Product:TooManyAttributes"] = SaveTimeReason,
        ["TradeXpress:Trendyol:Product:TooManyAttributes"] = SaveTimeReason,
        ["TradeXpress:N11:ProductVariant:OverrideRequiredForN11Only"] = SaveTimeReason,
        ["TradeXpress:Trendyol:ProductVariant:OverrideRequiredForTrendyolOnly"] = SaveTimeReason,

        // ══ KALICI: altyapı ön koşulu ══════════════════════════════════════════════════════════════════
        ["TradeXpress:N11:Product:RecordNotFound"] = PlumbingReason,
        ["TradeXpress:N11:Product:ChannelNotFound"] = PlumbingReason,
        ["TradeXpress:N11:Product:ProductNotFound"] = PlumbingReason,
        ["TradeXpress:N11:Product:CompanyRequired"] = PlumbingReason,
        ["TradeXpress:Trendyol:Product:RecordNotFound"] = PlumbingReason,
        ["TradeXpress:Trendyol:Product:ChannelNotFound"] = PlumbingReason,
        ["TradeXpress:Trendyol:Product:ProductNotFound"] = PlumbingReason,
        ["TradeXpress:Trendyol:Product:CompanyRequired"] = PlumbingReason,

        // ══ KALICI: yalnız gönderim anında bilinebilir ═════════════════════════════════════════════════
        // N11 kategori nitelik tanımı çekilemedi (HTTP/cache) · geçici görsel linki üretilemedi (dış barındırıcı).
        ["TradeXpress:N11:Product:CategoryAttributesUnavailable"] = RuntimeReason,
        ["TradeXpress:Trendyol:Product:ImageTemporaryLinkFailed"] = RuntimeReason,

        // ══ KALICI: teşhis ucu ═════════════════════════════════════════════════════════════════════════
        ["TradeXpress:N11:Rest:NoPendingTask"] = DiagnosticReason,
        ["TradeXpress:N11:Rest:NoSubmission"] = DiagnosticReason,
        ["TradeXpress:Trendyol:Product:NoBatch"] = DiagnosticReason,
    };

    // ── Testler ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Every_push_time_rule_is_either_shown_in_the_panel_or_justified_here()
    {
        var pushCodes = ReadPushCodes();

        var uncovered = pushCodes.Keys
            .Where(code => !PanelCounterparts.ContainsKey(code) && !AllowList.ContainsKey(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .Select(code => $"{code}  ({pushCodes[code]})")
            .ToList();

        uncovered.ShouldBeEmpty(
            "Push/sync yolunda kullanıcıyı durduran ama satışa hazırlık paneliyle İLİŞKİLENDİRİLMEMİŞ kod(lar) var. "
            + "Kullanıcı bu engeli ilk kez push anında görür. İki seçenekten birini yap ve KARARI yaz: "
            + $"① ProductSaleValidator'a dal ekle ve kodu {nameof(PanelCounterparts)}'a yaz, "
            + $"② panelde gösterilemiyorsa {nameof(AllowList)}'e gerekçesiyle ekle."
            + Environment.NewLine
            + string.Join(Environment.NewLine, uncovered));
    }

    [Fact]
    public void The_net_must_not_carry_entries_for_codes_that_no_longer_exist()
    {
        var pushCodes = ReadPushCodes();

        var stale = PanelCounterparts.Keys.Concat(AllowList.Keys)
            .Where(code => !pushCodes.ContainsKey(code))
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // Ölü satır, testi olduğundan geniş gösterir: silinmiş/yeniden adlandırılmış bir kod için "karar verilmiş"
        // görünür ve gerçek kodun kapsam dışı kaldığı fark edilmez.
        stale.ShouldBeEmpty(
            "Aşağıdaki kodlar artık push yolunda YOK ama bu dosyada hâlâ satırları var — yeniden adlandırıldıysa "
            + "satırı güncelle, kaldırıldıysa satırı sil:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void Mapped_panel_codes_must_exist_in_the_validator()
    {
        var panelCodes = ReadPanelIssueCodes();

        var missing = PanelCounterparts
            .Where(pair => !panelCodes.Contains(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} → {pair.Value}")
            .ToList();

        // Panel kodu yeniden adlandırılır/kaldırılırsa bu eşleme yalana döner: push engeli "panelde görünüyor"
        // sayılır ama görünmez. Bağ mekanik olarak kurulur ki sessizce kopamasın.
        missing.ShouldBeEmpty(
            $"Aşağıdaki eşlemelerin panel karşılığı {ValidatorPath} içinde YOK (yeniden adlandırılmış ya da "
            + "kaldırılmış olabilir):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void Every_push_validator_in_the_tree_is_covered_by_this_net()
    {
        var discovered = ConventionSource.EnumerateSource("*PushValidator.cs")
            .Select(ConventionSource.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var declared = PushPathPaths.ToHashSet(StringComparer.Ordinal);
        var uncovered = discovered.Where(p => !declared.Contains(p)).ToList();

        // Yeni bir kanalın push validator'ı eklendiğinde kapsam kendiliğinden genişlemez — bu fact onu yakalar.
        // (Etsy bilerek dışarıda: bugün Etsy'nin push yolu YOK, dolayısıyla push validator'ı da yok.)
        uncovered.ShouldBeEmpty(
            $"Ağaçta bu ağın taramadığı push validator dosyası var — {nameof(PushPathPaths)}'e ekle:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, uncovered));

        discovered.ShouldNotBeEmpty(
            "Hiç *PushValidator.cs bulunamadı — dosya adlandırma deseni değiştiyse bu arama BAYAT demektir.");
    }

    [Fact]
    public void Panel_gap_debt_is_counted_and_visible()
    {
        var gaps = AllowList
            .Where(pair => pair.Value == PanelGapReason)
            .Select(pair => pair.Key)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        // Bu fact BORCU KIRMIZI YAPMAZ (2026-08-21 erimesi sonrası 14 satır: tamamı nitelik/varyant-ekseni
        // ailesi — kanal attribute verisi ister; MixedCurrency ×2 panele alındı, NoSyncableSku ·
        // DuplicateStockCode · PriceOutOfBand kalıcı gerekçeye taşındı). İşi, borcun sayısını her koşuda
        // görünür kılmak ve borç SIFIRLANDIĞINDA PanelGapReason sabitinin ölü kalmasını engellemek: sayı 0'a
        // inince buradan haber alınır, sabit ve bu fact birlikte kaldırılır.
        gaps.Count.ShouldBeGreaterThan(
            0,
            "Panel boşluğu kalmamış görünüyor. Doğruysa: PanelGapReason sabitini ve bu fact'i kaldır, "
            + "CLAUDE.md'deki 'panel ⇄ push paritesi' borcunu kapat.");
    }

    // ── Kaynak okuma ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Push kodu → onu barındıran dosya (hata mesajında nereye bakılacağı yazsın diye).</summary>
    private static Dictionary<string, string> ReadPushCodes()
    {
        var codes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var relative in PushPathPaths)
        {
            var text = ReadSource(relative);
            var found = PushExceptionCodeRegex.Matches(text)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // DOSYA BAŞINA taban: tek bir dosyanın deseni değişince (ör. kodlar bir sabit sınıfına taşınınca)
            // toplam sayı hâlâ yüksek kalır ve kayıp fark edilmez. Bu yüzden kontrol dosya seviyesindedir.
            found.ShouldNotBeEmpty(
                $"{relative} içinde hiç push exception kodu bulunamadı — REGEX BAYAT OLABİLİR. "
                + "Kodlar bir sabit sınıfına taşındıysa PushExceptionCodeRegex ve PushPathPaths güncellenmeli; "
                + "bu fact'i gevşetme, ağ tamamen sessizleşir.");

            foreach (var code in found)
            {
                codes[code] = relative;
            }
        }

        return codes;
    }

    private static HashSet<string> ReadPanelIssueCodes()
    {
        var text = ReadSource(ValidatorPath);
        var codes = PanelIssueCodeRegex.Matches(text)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Taban sayı: bugün 22 issue kodu var. Kural silinerek değil TAŞINARAK azalabilir; 10'un altına düşmek
        // gerçek bir kaldırmadan çok "sabit deseni değişti" demektir ve o hâlde eşleme kontrolü boşa çalışırdı.
        codes.Count.ShouldBeGreaterThanOrEqualTo(
            10,
            $"{ValidatorPath} içinde beklenenden az issue kodu bulundu ({codes.Count}) — PanelIssueCodeRegex "
            + "BAYAT OLABİLİR (sabitler enum/record'a taşınmış olabilir).");

        return codes;
    }

    private static string ReadSource(string relativePath)
    {
        var path = Path.Combine(ConventionSource.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(path).ShouldBeTrue(
            $"{relativePath} bulunamadı — dosya taşındıysa bu testin yolu güncellenmeli. "
            + "Yol bayatladığında ağ hiçbir şey taramaz.");

        return File.ReadAllText(path);
    }
}
