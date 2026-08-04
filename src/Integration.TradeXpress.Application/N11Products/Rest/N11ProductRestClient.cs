using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 REST <b>yazma</b> uçları — ürün oluşturma / ürün bilgisi güncelleme / fiyat-stok güncelleme.
/// <para><b>Üç uç da ASENKRONdur:</b> HTTP 200 "işlem başarılı" DEĞİL, "kuyruğa alındı" demektir; yanıt bir
/// <c>taskId</c> döner ve gerçek sonuç <c>task-details/page-query</c> ile (N11TaskPoller) SKU bazında sorgulanır.</para>
/// <para><b>1000 SKU sınırı çağırana yıkılmaz:</b> istemci listeyi 1000'lik dilimlere kendisi böler ve her dilim için
/// ayrı istek atar ⇒ dönüş tipi tek <c>N11TaskSubmission</c> değil <b>listesidir</b> (main'in sabitlediği sözleşme bu
/// yönde genişletildi; alternatifi çağıranın parçalama sorumluluğunu üstlenmesiydi — aynı kural her çağrı yerinde
/// tekrarlanırdı, DRY ihlali).</para>
/// <para>Mevcut SOAP yolu (<c>IN11ProductClient</c>) DEĞİŞMEDİ; bu tür yalnız REST yolunu <b>ekler</b>.</para>
/// </summary>
public interface IN11ProductRestClient : ITransientDependency
{
    /// <summary>Ürünleri N11'e yükler (<c>tasks/product-create</c>). Her 1000 SKU için bir task döner.
    /// Yerel doğrulama (zorunlu alan / KDV / fiyat / görsel https) başarısızsa hiç istek atılmaz.</summary>
    Task<IReadOnlyList<N11TaskSubmission>> CreateProductsAsync(
        IReadOnlyList<N11RestProductCreate> products, string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Ürün bilgisi (fiyat/stok DIŞI) günceller (<c>tasks/product-update</c>). Gönderilmeyen alan güncellenmez.</summary>
    Task<IReadOnlyList<N11TaskSubmission>> UpdateProductsAsync(
        IReadOnlyList<N11RestProductUpdate> updates, string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Fiyat ve/veya stok günceller (<c>tasks/price-stock-update</c>). Yalnız stok göndermek serbesttir;
    /// fiyat gönderilecekse listPrice+salePrice BİRLİKTE zorunludur.</summary>
    Task<IReadOnlyList<N11TaskSubmission>> UpdatePriceStockAsync(
        IReadOnlyList<N11RestPriceStock> items, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IN11ProductRestClient"/> uygulaması. Kimlik <c>appkey</c>/<c>appsecret</c> HTTP başlığıyla taşınır
/// (taban sınıf halleder); sır ASLA loglanmaz. Gövde: <c>{"payload":{"integrator":"...","skus":[...]}}</c>.
/// <para><b>Fail-fast felsefesi:</b> dokümanın REJECT sebebi olarak saydığı her kural (fiyat çifti, 2 hane küsurat,
/// listPrice ≥ salePrice, KDV oranı, https görsel, stok kodu uzunluğu, sessiz no-op bayrakları) uzak REJECT
/// beklenmeden YERELDE yakalanır — asenkron uçta uzak hata ancak poll sonrası görülebilir, o da geç ve pahalıdır.</para>
/// </summary>
public sealed class N11ProductRestClient : N11RestClientBase, IN11ProductRestClient
{
    // Taban adres N11RestClientBase'in (Paket A) SSOT'udur; burada yalnız yol ekleniyor.
    // Bilinçli 'static readonly' (const değil): taban sembolün const mü static readonly mi olduğuna bağımlı kalmayalım.
    private static readonly string CreateUrl = RestProductBase + "/tasks/product-create";
    private static readonly string UpdateUrl = RestProductBase + "/tasks/product-update";
    private static readonly string PriceStockUrl = RestProductBase + "/tasks/price-stock-update";

    private static readonly HashSet<string> AllowedCurrencies = new(StringComparer.Ordinal) { "TL", "USD", "EUR" };
    // KDV kümesi Domain.Shared'daki SSOT'tan okunur — entity guard'ı (SetVatRate) ile aynı listeye bakalım ki
    // formda kabul edilen bir oran push'ta reddedilmesin (ya da tersi).
    private static readonly IReadOnlyCollection<int> AllowedVatRates = N11ProductConsts.AllowedVatRates;
    private static readonly HashSet<string> AllowedProductStatuses = new(StringComparer.Ordinal) { "Active", "Suspended" };

    /// <summary>
    /// Yazma gövdesinin serileştirme sözleşmesi = <b>tabanın ortak sözleşmesi + fiyat biçimi</b>.
    /// Kopya kurucu bilinçli: camelCase / null-atlama kuralları <see cref="N11RestClientBase.JsonOptions"/>'ta TEK
    /// kaynaktan gelir, burada yalnızca tabanın kapsam dışı bıraktığı "noktadan sonra tam 2 hane" kuralı
    /// (<see cref="N11PriceJsonConverter"/>) eklenir — ayarları yeniden yazmak SSOT'u ikizlerdi.
    /// Varsayılan (kaçışlı) encoder korunur: açıklama HTML içerdiğinden <c>&lt;</c>/<c>&amp;</c> kaçışı zararsızdır,
    /// karşı taraf çözdüğünde birebir aynı metni görür.
    /// </summary>
    private static readonly JsonSerializerOptions WriteJsonOptions = new(JsonOptions)
    {
        Converters = { new N11PriceJsonConverter() },
    };

    private readonly ILogger<N11ProductRestClient> _logger;

    public N11ProductRestClient(ILogger<N11ProductRestClient> logger)
    {
        _logger = logger;
    }

    // ── Yazma uçları ────────────────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<N11TaskSubmission>> CreateProductsAsync(
        IReadOnlyList<N11RestProductCreate> products, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Check.NotNull(products, nameof(products));
        ValidateCreates(products);
        return SubmitAsync(CreateUrl, products, appKey, appSecret, cancellationToken);
    }

    public Task<IReadOnlyList<N11TaskSubmission>> UpdateProductsAsync(
        IReadOnlyList<N11RestProductUpdate> updates, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Check.NotNull(updates, nameof(updates));
        ValidateUpdates(updates);
        return SubmitAsync(UpdateUrl, updates, appKey, appSecret, cancellationToken);
    }

    public Task<IReadOnlyList<N11TaskSubmission>> UpdatePriceStockAsync(
        IReadOnlyList<N11RestPriceStock> items, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Check.NotNull(items, nameof(items));
        ValidatePriceStocks(items);
        return SubmitAsync(PriceStockUrl, items, appKey, appSecret, cancellationToken);
    }

    // ── Gönderim ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Satırları 1000'lik dilimlere bölüp her dilimi ayrı task olarak gönderir (N11: "tek seferde maksimum
    /// 1000 sku"). Dilimler SIRAYLA gider — paralel gönderim N11'in hız sınırına takılma riskini artırırdı.</summary>
    private async Task<IReadOnlyList<N11TaskSubmission>> SubmitAsync<TSku>(
        string url, IReadOnlyList<TSku> rows, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<N11TaskSubmission>();   // gönderilecek satır yok ⇒ boşuna istek atma
        }

        var submissions = new List<N11TaskSubmission>();
        foreach (var chunk in rows.Chunk(N11RestConsts.MaxSkusPerRequest))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var envelope = new N11RestEnvelope<TSku>(new N11RestPayload<TSku>(N11RestConsts.Integrator, chunk));
            var json = JsonSerializer.Serialize(envelope, WriteJsonOptions);
            var body = await RestSendAsync(HttpMethod.Post, url, json, appKey, appSecret, cancellationToken);
            submissions.Add(ParseSubmission(body, url, chunk.Length));
        }

        return submissions;
    }

    /// <summary>Yazma yanıtını (<c>{id, type, status, reasons[]}</c>) çözer. <c>id</c> yoksa pollanacak bir şey de
    /// yoktur ⇒ fail-fast. <c>REJECT</c> statüsü tipli sonuçta yalnız ham metin olarak taşındığından sebepler
    /// (kaybolmasın diye) burada loglanır.</summary>
    private N11TaskSubmission ParseSubmission(string body, string url, int skuCount)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            // HTTP 200 + JSON olmayan gövde (hata sayfası/proxy yanıtı). Kök nedeni gizlemeden anlaşılır hataya çevir.
            throw new BusinessException("TradeXpress:N11:Rest:TaskNotAccepted", innerException: ex)
                .WithData("Url", url)
                .WithData("Status", "UNPARSABLE")
                .WithData("Reasons", body.Length > 500 ? body[..500] : body);
        }

        using (doc)
        {
            return ReadSubmission(doc.RootElement, url, skuCount);
        }
    }

    /// <summary><c>{id, type, status, reasons[]}</c> yanıtını <c>N11TaskSubmission</c>'a çevirir.</summary>
    private N11TaskSubmission ReadSubmission(JsonElement root, string url, int skuCount)
    {
        var taskId = ReadTaskId(root);
        var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
        var reasons = ReadReasons(root);

        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new BusinessException("TradeXpress:N11:Rest:TaskNotAccepted")
                .WithData("Url", url)
                .WithData("Status", status ?? string.Empty)
                .WithData("Reasons", reasons);
        }

        // Statü yorumu N11TaskStates'in (SSOT) işi — ham dizgi burada tekrar edilmez.
        if (N11TaskStates.Parse(status) == N11TaskState.Rejected)
        {
            _logger.LogWarning(
                "N11 REST yazma task'ı REJECT aldı ({Url}, taskId={TaskId}, {SkuCount} sku): {Reasons}",
                url, taskId, skuCount, reasons);
        }

        return new N11TaskSubmission(taskId, status ?? string.Empty);
    }

    private static string? ReadTaskId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind switch
        {
            JsonValueKind.Number => id.GetInt64().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => id.GetString(),
            _ => null,
        };
    }

    private static string ReadReasons(JsonElement root)
    {
        if (!root.TryGetProperty("reasons", out var reasons) || reasons.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(" | ", reasons.EnumerateArray().Select(r => r.ToString()));
    }

    // ── Yerel doğrulama: product-create ─────────────────────────────────────────────────────────────

    private static void ValidateCreates(IReadOnlyList<N11RestProductCreate> products)
    {
        RequireUniqueStockCodes(products.Select(p => p.StockCode));

        foreach (var p in products)
        {
            RequireStockCode(p.StockCode);
            RequireText(p.Title, "title", p.StockCode);
            RequireText(p.Description, "description", p.StockCode);
            RequireText(p.ProductMainId, "productMainId", p.StockCode);
            RequireText(p.ShipmentTemplate, "shipmentTemplate", p.StockCode);
            RequireCurrency(p.CurrencyType, p.StockCode);
            RequireVatRate(p.VatRate, p.StockCode);

            if (p.CategoryId <= 0)
            {
                // Ürün eklemede yalnız EN ALT KIRILIM (yaprak) kategori kabul edilir; id'nin yapraklığını N11 denetler.
                throw Fail("TradeXpress:N11:Rest:CategoryIdInvalid", p.StockCode).WithData("CategoryId", p.CategoryId);
            }

            RequirePreparingDay(p.PreparingDay, p.StockCode);
            RequireQuantity(p.Quantity, p.StockCode);
            RequirePricePair(p.ListPrice, p.SalePrice, p.StockCode);

            if (p.MaxPurchaseQuantity is <= 0)
            {
                throw Fail("TradeXpress:N11:Rest:MaxPurchaseQuantityInvalid", p.StockCode)
                    .WithData("MaxPurchaseQuantity", p.MaxPurchaseQuantity!.Value);
            }

            RequireImages(p);
            RequireAttributes(p);
        }
    }

    /// <summary>Görsel kuralı: dizi ZORUNLUdur (Mod 3'te bile boş dizi olarak gönderilir) ve boş kalabilmesi için
    /// catalogId ya da barcode ile hızlı yükleme yapılıyor olması gerekir. Her URL <b>https</b> olmalıdır.</summary>
    private static void RequireImages(N11RestProductCreate p)
    {
        if (p.Images is null || (p.Images.Count == 0 && p.CatalogId is null && string.IsNullOrWhiteSpace(p.Barcode)))
        {
            throw Fail("TradeXpress:N11:Rest:ImagesRequired", p.StockCode);
        }

        foreach (var image in p.Images)
        {
            if (string.IsNullOrWhiteSpace(image.Url) || !image.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw Fail("TradeXpress:N11:Rest:ImageUrlNotHttps", p.StockCode).WithData("Url", image.Url ?? string.Empty);
            }
        }
    }

    /// <summary>Attribute dizisi ZORUNLUdur (hızlı yüklemede boş olabilir ama gönderilir); değer tarafında
    /// valueId ya da customValue'dan en az biri dolu olmalıdır — ikisi de boşsa özellik anlamsızdır.</summary>
    private static void RequireAttributes(N11RestProductCreate p)
    {
        if (p.Attributes is null)
        {
            throw Fail("TradeXpress:N11:Rest:AttributesRequired", p.StockCode);
        }

        foreach (var attribute in p.Attributes)
        {
            if (attribute.Id <= 0 || (attribute.ValueId is null && string.IsNullOrWhiteSpace(attribute.CustomValue)))
            {
                throw Fail("TradeXpress:N11:Rest:AttributeValueRequired", p.StockCode).WithData("AttributeId", attribute.Id);
            }
        }
    }

    // ── Yerel doğrulama: product-update ─────────────────────────────────────────────────────────────

    private static void ValidateUpdates(IReadOnlyList<N11RestProductUpdate> updates)
    {
        RequireUniqueStockCodes(updates.Select(u => u.StockCode));

        foreach (var u in updates)
        {
            RequireStockCode(u.StockCode);

            if (u.VatRate is not null)
            {
                RequireVatRate(u.VatRate.Value, u.StockCode);
            }

            if (u.CurrencyType is not null)
            {
                RequireCurrency(u.CurrencyType, u.StockCode);
            }

            if (u.PreparingDay is not null)
            {
                RequirePreparingDay(u.PreparingDay.Value, u.StockCode);
            }

            if (u.Status is not null && !AllowedProductStatuses.Contains(u.Status))
            {
                throw Fail("TradeXpress:N11:Rest:ProductStatusInvalid", u.StockCode).WithData("Status", u.Status);
            }

            if (u.MaxPurchaseQuantity is <= 0)
            {
                throw Fail("TradeXpress:N11:Rest:MaxPurchaseQuantityInvalid", u.StockCode)
                    .WithData("MaxPurchaseQuantity", u.MaxPurchaseQuantity!.Value);
            }

            // SESSİZ NO-OP → GÜRÜLTÜLÜ HATA: N11 bu iki alanı yalnız delete* bayrağı true iken günceller,
            // bayrak yokken hata da dönmez. Sessizce kaybolan güncellemeyi burada yakalıyoruz.
            if (u.ProductMainId is not null && u.DeleteProductMainId != true)
            {
                throw Fail("TradeXpress:N11:Rest:ProductMainIdUpdateNeedsFlag", u.StockCode);
            }

            if (u.MaxPurchaseQuantity is not null && u.DeleteMaxPurchaseQuantity != true)
            {
                throw Fail("TradeXpress:N11:Rest:MaxPurchaseQuantityUpdateNeedsFlag", u.StockCode);
            }

            var hasAnyChange = u.Status is not null || u.PreparingDay is not null || u.ShipmentTemplate is not null
                || u.CurrencyType is not null || u.ProductMainId is not null || u.MaxPurchaseQuantity is not null
                || u.Description is not null || u.VatRate is not null
                || u.DeleteProductMainId == true || u.DeleteMaxPurchaseQuantity == true;

            if (!hasAnyChange)
            {
                // Yalnız stockCode gönderilen istek N11'de sessiz no-op olurdu; boşuna task açmayalım.
                throw Fail("TradeXpress:N11:Rest:NothingToUpdate", u.StockCode);
            }
        }
    }

    // ── Yerel doğrulama: price-stock-update ─────────────────────────────────────────────────────────

    private static void ValidatePriceStocks(IReadOnlyList<N11RestPriceStock> items)
    {
        RequireUniqueStockCodes(items.Select(i => i.StockCode));

        foreach (var i in items)
        {
            RequireStockCode(i.StockCode);

            if (i.CurrencyType is not null)
            {
                RequireCurrency(i.CurrencyType, i.StockCode);
            }

            if (i.Quantity is not null)
            {
                RequireQuantity(i.Quantity.Value, i.StockCode);
            }

            RequirePricePair(i.ListPrice, i.SalePrice, i.StockCode);

            if (i.ListPrice is null && i.SalePrice is null && i.Quantity is null && i.CurrencyType is null)
            {
                throw Fail("TradeXpress:N11:Rest:NothingToUpdate", i.StockCode);
            }
        }
    }

    // ── Ortak guard'lar ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Fiyat çifti kuralı: doküman "listPrice ve salePrice birlikte gönderilmelidir" der; yalnız biri dolu
    /// olan istek REJECT alır. Karşılaştırma N11'e GİDECEK (2 haneye normalize edilmiş) değerler üzerinden yapılır —
    /// aksi hâlde yuvarlama sonrası sıra bozulup uzak REJECT'e düşebilirdi. Eşitlik serbesttir, listPrice &lt; salePrice değil.</summary>
    private static void RequirePricePair(decimal? listPrice, decimal? salePrice, string stockCode)
    {
        if (listPrice is null && salePrice is null)
        {
            return;   // fiyat güncellenmiyor (yalnız stok / yalnız bilgi güncellemesi) — geçerli
        }

        if (listPrice is null || salePrice is null)
        {
            throw Fail("TradeXpress:N11:Rest:PriceFieldsMustBePaired", stockCode);
        }

        if (listPrice.Value < 0m || salePrice.Value < 0m)
        {
            throw Fail("TradeXpress:N11:Rest:PriceNegative", stockCode);
        }

        var list = N11RestPrice.Normalize(listPrice.Value);
        var sale = N11RestPrice.Normalize(salePrice.Value);
        if (list < sale)
        {
            throw Fail("TradeXpress:N11:Rest:ListPriceBelowSalePrice", stockCode)
                .WithData("ListPrice", N11RestPrice.Format(listPrice.Value))
                .WithData("SalePrice", N11RestPrice.Format(salePrice.Value));
        }
    }

    private static void RequireStockCode(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode))
        {
            throw new BusinessException("TradeXpress:N11:Rest:FieldRequired")
                .WithData("Field", "stockCode")
                .WithData("StockCode", string.Empty);
        }

        if (stockCode.Length > N11RestConsts.MaxStockCodeLength)
        {
            throw Fail("TradeXpress:N11:Rest:StockCodeTooLong", stockCode)
                .WithData("Max", N11RestConsts.MaxStockCodeLength);
        }
    }

    /// <summary>Aynı istekte aynı stok kodu iki kez gönderilirse hangi satırın kazandığı belirsizdir (N11 SKU'yu
    /// stockCode ile adresler) ⇒ sessiz veri kaybı yerine fail-fast.</summary>
    private static void RequireUniqueStockCodes(IEnumerable<string> stockCodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in stockCodes)
        {
            if (!string.IsNullOrWhiteSpace(code) && !seen.Add(code))
            {
                throw Fail("TradeXpress:N11:Rest:DuplicateStockCode", code);
            }
        }
    }

    private static void RequireText(string? value, string field, string stockCode)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw Fail("TradeXpress:N11:Rest:FieldRequired", stockCode).WithData("Field", field);
        }
    }

    /// <summary>Para birimi TL/USD/EUR olmalıdır. Küçük harf gönderimi sessizce düzeltilmez — girdi olduğu gibi
    /// gider, bu yüzden yanlış yazım burada durur (least-astonishment: istemci veriyi değiştirmez).</summary>
    private static void RequireCurrency(string currencyType, string stockCode)
    {
        if (!AllowedCurrencies.Contains(currencyType))
        {
            throw Fail("TradeXpress:N11:Rest:CurrencyTypeInvalid", stockCode).WithData("CurrencyType", currencyType);
        }
    }

    private static void RequireVatRate(int vatRate, string stockCode)
    {
        if (!AllowedVatRates.Contains(vatRate))
        {
            throw Fail("TradeXpress:N11:Rest:VatRateInvalid", stockCode).WithData("VatRate", vatRate);
        }
    }

    private static void RequireQuantity(int quantity, string stockCode)
    {
        if (quantity < 0 || quantity > N11RestConsts.MaxQuantity)
        {
            throw Fail("TradeXpress:N11:Rest:QuantityOutOfRange", stockCode)
                .WithData("Quantity", quantity)
                .WithData("Max", N11RestConsts.MaxQuantity);
        }
    }

    private static void RequirePreparingDay(int preparingDay, string stockCode)
    {
        if (preparingDay < 1)
        {
            throw Fail("TradeXpress:N11:Rest:PreparingDayInvalid", stockCode).WithData("PreparingDay", preparingDay);
        }
    }

    /// <summary>Hata kurucusu — her yerel guard hatası hangi SKU'da patladığını taşır (1000 satırlık gönderimde
    /// tek satırı bulmak aksi hâlde imkânsız olurdu).</summary>
    private static BusinessException Fail(string code, string stockCode)
    {
        return new BusinessException(code).WithData("StockCode", stockCode);
    }
}
