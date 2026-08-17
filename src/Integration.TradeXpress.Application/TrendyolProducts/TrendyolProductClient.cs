using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="ITrendyolProductClient"/> — Trendyol Marketplace API v2 (REST/JSON). Auth + gönderim ORTAK TABANDAN
/// (<see cref="TrendyolRestClientBase"/> — kategori/marka istemcileriyle aynı; eski yerel auth kopyası teknik borçtu,
/// kaldırıldı). Ürün oluşturma ASENKRON (submit → batchRequestId; durum ayrı sorgu); satıcı ürün listesi salt GET.
///
/// <para><b>⚠ Create/fiyat-stok/durum uçları CANLI DOĞRULANMADI</b> (bu oturumda gerçek satıcı kimliği yok);
/// listeleme ucu (<c>GET /integration/product/sellers/{sellerId}/products</c>) kimlik doğrulayıcıda status-probe
/// olarak KANITLI. Gerekirse bu tek dosyadaki sabitleri güncellemek yeterlidir (model/AppService değişmeden):</para>
/// <list type="bullet">
/// <item>Create: <c>POST /integration/product/sellers/{sellerId}/products</c> · gövde <c>{ "items": [ ... ] }</c> → <c>{ "batchRequestId": "..." }</c>.</item>
/// <item>Fiyat/stok: <c>POST /integration/inventory/sellers/{sellerId}/products/price-and-inventory</c> ·
/// gövde <c>{ "items": [ { barcode, quantity?, listPrice?, salePrice? } ] }</c> → <c>{ "batchRequestId": "..." }</c>.
/// <b>URL AİLESİ FARKLI</b> (<c>/inventory/</c>, create'teki <c>/product/</c> değil) → create URL'inden türetilmez.
/// <b>Bu batch'te kök <c>status</c> alanı DÖNMEYEBİLİR</b> — sonuç item-bazlı statülerden okunur.</item>
/// <item>Durum: <c>GET /integration/product/sellers/{sellerId}/products/batch-requests/{batchRequestId}</c>.</item>
/// <item>Listeleme: <c>GET /integration/product/sellers/{sellerId}/products?page=&amp;size=</c> →
/// <c>{ totalElements, totalPages, page, size, content: [ ... ] }</c>.</item>
/// </list>
/// </summary>
public sealed class TrendyolProductClient : TrendyolRestClientBase, ITrendyolProductClient, ITransientDependency
{
    /// <summary>Sayfalama döngüsü güvenlik tavanı — totalPages bozuk/aşırı dönerse sonsuz döngüye girilmez.</summary>
    private const int MaxPageLoops = 500;

    /// <summary>Trendyol tek fiyat/stok isteğinde en fazla 1000 kalem kabul eder (doküman). Aşılırsa SESSİZ
    /// kırpma ya da otomatik dilimleme YOK — dostane hata. Dilimlemiyoruz çünkü <c>SalesChannelTrTrendyolProduct</c>
    /// kayıt başına TEK <c>BatchRequestId</c> taşıyor; N makbuz bu modele sığmaz ve hangi dilimin battığı
    /// izlenemez hâle gelirdi.</summary>
    private const int MaxItemsPerRequest = 1000;

    public async Task<TrendyolSubmitResult> SubmitProductAsync(
        TrendyolProductData product, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        var body = BuildCreateBody(product);
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products";

        var request = CreateRequest(HttpMethod.Post, url, credentials);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var (ok, status, payload) = await SendAsync(request, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:SubmitFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        var batchRequestId = ReadString(payload, "batchRequestId");
        return new TrendyolSubmitResult(batchRequestId);
    }

    public async Task<TrendyolSubmitResult> UpdatePriceAndInventoryAsync(
        IReadOnlyList<TrendyolPriceInventoryItem> items, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        // Guard'lar gövde kurucusunda, yani AĞA ÇIKMADAN önce koşar: bozuk satır Trendyol'a hiç gitmez.
        var body = BuildPriceInventoryBody(items);

        // ⚠ URL ailesi create'ten FARKLI: /integration/inventory/... (create /integration/product/...).
        var url = $"{BaseUrl}/integration/inventory/sellers/{credentials.SellerId}/products/price-and-inventory";

        // 'using' ile SARMA — SendAsync isteğin sahipliğini alır (taban deseni; create yolu da böyle).
        var request = CreateRequest(HttpMethod.Post, url, credentials);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // Yazma ucu → GET-retry KULLANILMAZ (idempotent değil; ayrıca aynı gövde 15 dk içinde tekrarlanamıyor).
        var (ok, status, payload) = await SendAsync(request, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:PriceInventoryFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        var batchRequestId = ReadString(payload, "batchRequestId");
        if (string.IsNullOrWhiteSpace(batchRequestId))
        {
            // BİLİNÇLİ SAPMA: create yolu makbuzsuz yanıta TOLERANSLI, burası değil. Create'te makbuz kaybı
            // yalnız durum sorgusunu köreltir; hafif senkronda LastSent* zincirini KİLİTLER — kayıt her turda
            // "değişti" görünüp aynı isteği yeniden gönderir ve 15 dk mükerrer reddine çarpar. Sessiz sonsuz
            // döngü yerine burada duruyoruz. (Create yolunun davranışı DEĞİŞTİRİLMEDİ.)
            throw new BusinessException("TradeXpress:Trendyol:Product:BatchIdMissing")
                .WithData("body", Truncate(payload));
        }

        return new TrendyolSubmitResult(batchRequestId);
    }

    public async Task<TrendyolSubmitResult> DeleteProductsAsync(
        IReadOnlyList<string> barcodes, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (barcodes.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DeleteNeedsBarcodes");
        }

        var body = BuildDeleteBody(barcodes);
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products";

        var request = CreateRequest(HttpMethod.Delete, url, credentials);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // Yazma ucu → GET-retry KULLANILMAZ (idempotent sayılmaz; create/price yollarıyla aynı disiplin).
        var (ok, status, payload) = await SendAsync(request, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DeleteFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        return new TrendyolSubmitResult(ReadString(payload, "batchRequestId"));
    }

    public async Task<TrendyolSubmitResult> ArchiveProductsAsync(
        IReadOnlyList<string> barcodes, bool archived, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (barcodes.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ArchiveNeedsBarcodes");
        }

        var body = BuildArchiveBody(barcodes, archived);
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products/archive-state";

        var request = CreateRequest(HttpMethod.Put, url, credentials);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var (ok, status, payload) = await SendAsync(request, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ArchiveFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        // Doküman başarı gövdesi örneği vermiyor; batch id varsa okunur (create yolu gibi makbuzsuz yanıta toleranslı).
        return new TrendyolSubmitResult(ReadString(payload, "batchRequestId"));
    }

    // internal — gövde ağa çıkmadan birim test edilebilsin (create/price gövdeleriyle aynı desen).
    internal static string BuildArchiveBody(IReadOnlyList<string> barcodes, bool archived)
    {
        var root = new Dictionary<string, object?>
        {
            ["items"] = barcodes.Select(b => new Dictionary<string, object?> { ["barcode"] = b, ["archived"] = archived }).ToList(),
        };
        return JsonSerializer.Serialize(root);
    }

    // internal — gövde ağa çıkmadan birim test edilebilsin (create/price gövdeleriyle aynı desen).
    internal static string BuildDeleteBody(IReadOnlyList<string> barcodes)
    {
        var root = new Dictionary<string, object?>
        {
            ["items"] = barcodes.Select(b => new Dictionary<string, object?> { ["barcode"] = b }).ToList(),
        };
        return JsonSerializer.Serialize(root);
    }

    public async Task<TrendyolBatchStatus> GetBatchStatusAsync(
        string batchRequestId, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products/batch-requests/{batchRequestId}";

        var (ok, status, payload) = await SendGetWithRetryAsync(url, credentials, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:StatusFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        return ParseBatchStatus(payload);
    }

    // ── Satıcı ürün listesi (salt GET — import kaynağı) ──────────────────────────────────────────────

    public async Task<TrendyolSellerProductsPage> GetSellerProductsAsync(
        TrendyolCredentials credentials, int page, int size, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products?page={page}&size={size}";

        var (ok, status, payload) = await SendGetWithRetryAsync(url, credentials, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ListFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        return ParseSellerProductsPage(page, size, payload);
    }

    public async Task<IReadOnlyList<TrendyolRemoteProduct>> GetAllSellerProductsAsync(
        TrendyolCredentials credentials, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        var flat = await FetchAllPagesAsync(page => GetSellerProductsAsync(credentials, page, pageSize, cancellationToken));
        return GroupByProductMainId(flat);
    }

    /// <summary>Sayfalama döngüsü (public static — birim test edilir; sayfa kaynağı delege): totalPages'e kadar
    /// sırayla çeker (TrendyolBrandClient sayfalama deseni). Güvenlik tavanı (<see cref="MaxPageLoops"/>) aşılırsa
    /// SESSİZCE kısmî liste dönmek yerine dostane hata — sessiz kapsam düşürme yasak; import upsert-only olduğundan
    /// yeniden deneme güvenlidir.</summary>
    public static async Task<List<TrendyolRemoteProduct>> FetchAllPagesAsync(
        Func<int, Task<TrendyolSellerProductsPage>> fetchPage)
    {
        var flat = new List<TrendyolRemoteProduct>();
        var page = 0;
        int totalPages;
        do
        {
            var result = await fetchPage(page);
            flat.AddRange(result.Items);
            totalPages = result.TotalPages;
            page++;
        }
        while (page < totalPages && page < MaxPageLoops);

        if (page < totalPages)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:PageLimitExceeded")
                .WithData("totalPages", totalPages)
                .WithData("maxPages", MaxPageLoops);
        }

        return flat;
    }

    /// <summary>DÜZ kalemleri (barcode başına) Trendyol grup anahtarına (<c>productMainId</c>) göre birleştirir:
    /// ortak alanlar İLK kalemden, varyantlar geliş sırasıyla. productMainId boş kalem KENDİ BAŞINA üründür.</summary>
    public static IReadOnlyList<TrendyolRemoteProduct> GroupByProductMainId(IReadOnlyList<TrendyolRemoteProduct> flatItems)
    {
        var grouped = new List<TrendyolRemoteProduct>();
        var byMainId = new Dictionary<string, int>(StringComparer.Ordinal);   // productMainId → grouped index

        foreach (var item in flatItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProductMainId))
            {
                grouped.Add(item);
                continue;
            }

            if (byMainId.TryGetValue(item.ProductMainId!, out var index))
            {
                var existing = grouped[index];
                grouped[index] = existing with { Variants = existing.Variants.Concat(item.Variants).ToList() };
            }
            else
            {
                byMainId[item.ProductMainId!] = grouped.Count;
                grouped.Add(item);
            }
        }

        return grouped;
    }

    /// <summary>Listeleme yanıtını parse eder (public static — birim test edilir): sayfalama zarfı + <c>content[]</c>
    /// kalemleri. Barcode'suz kalem BOŞ barcode ile taşınır (parse'ta sessiz elenmez — import tarafı atla+raporla
    /// yapar, "InvalidBarcode" satırı rapora düşer). Bozuk JSON gövdesi dostane hatayla İMPORTU DURDURUR: sessizce
    /// boş sayfa dönmek o ve sonraki sayfaların kalemlerini raporsuz kaybettirirdi (sessiz kapsam düşürme yasak).
    /// Alan adları defansif okunur (sayı/metin id toleransı, onSale/onsale iki yazım).</summary>
    public static TrendyolSellerProductsPage ParseSellerProductsPage(int page, int size, string payload)
    {
        var items = new List<TrendyolRemoteProduct>();
        int totalPages;
        long totalElements;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            totalPages = ReadInt(root, "totalPages") ?? 0;
            totalElements = ReadLong(root, "totalElements") ?? 0;

            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in content.EnumerateArray())
                {
                    items.Add(ReadRemoteItem(el));
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ListParseFailed")
                .WithData("page", page);
        }

        return new TrendyolSellerProductsPage(page, size, totalPages, totalElements, items);
    }

    // Tek content[] öğesi → tek-varyantlı TrendyolRemoteProduct (gruplama üstte). Barcode yoksa BOŞ string taşınır
    // (import tarafı atla+raporla yapar — parse'ta sessiz kayıp yok).
    private static TrendyolRemoteProduct ReadRemoteItem(JsonElement el)
    {
        var barcode = ReadString(el, "barcode");

        var images = new List<string>();
        if (el.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
        {
            foreach (var img in imgs.EnumerateArray())
            {
                var imgUrl = img.ValueKind == JsonValueKind.Object ? ReadString(img, "url") : null;
                if (!string.IsNullOrWhiteSpace(imgUrl))
                {
                    images.Add(imgUrl!);
                }
            }
        }

        var attributes = new List<TrendyolRemoteAttribute>();
        if (el.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in attrs.EnumerateArray())
            {
                if (ReadInt(attr, "attributeId") is { } attributeId)
                {
                    attributes.Add(new TrendyolRemoteAttribute(
                        attributeId,
                        ReadString(attr, "attributeName"),
                        ReadInt(attr, "attributeValueId"),
                        ReadString(attr, "attributeValue"),
                        ReadString(attr, "customAttributeValue")));
                }
            }
        }

        var variant = new TrendyolRemoteVariant(
            Barcode: barcode?.Trim() ?? string.Empty,
            StockCode: ReadString(el, "stockCode"),
            Quantity: ReadInt(el, "quantity") ?? 0,
            ListPrice: ReadDecimal(el, "listPrice"),
            SalePrice: ReadDecimal(el, "salePrice"),
            ProductContentId: ReadLong(el, "productContentId") ?? ReadLong(el, "id"),
            Approved: ReadBool(el, "approved"),
            OnSale: ReadBool(el, "onSale") ?? ReadBool(el, "onsale"),
            Attributes: attributes,
            Flags: ReadListingFlags(el));

        return new TrendyolRemoteProduct(
            ProductMainId: ReadString(el, "productMainId"),
            Title: ReadString(el, "title") ?? string.Empty,
            Description: ReadString(el, "description"),
            CategoryId: ReadIdAsString(el, "pimCategoryId") ?? ReadIdAsString(el, "categoryId"),
            CategoryName: ReadString(el, "categoryName"),
            BrandId: ReadIdAsString(el, "brandId"),
            BrandName: ReadString(el, "brand"),
            VatRate: ReadInt(el, "vatRate"),
            DimensionalWeight: ReadDecimal(el, "dimensionalWeight"),
            DeliveryDuration: ReadInt(el, "deliveryDuration"),
            ImageUrls: images,
            Variants: new List<TrendyolRemoteVariant> { variant });
    }

    // Pazaryerinin ENGEL/DURUM beyanı. Bu alanlar yanıtta hep vardı ama okunmuyordu; okunmayan bir engel,
    // gönderim reddedilene kadar görünmez kalıyordu. Alan gelmezse null taşınır ("bildirilmedi" ≠ "engel yok").
    private static TrendyolRemoteListingFlags ReadListingFlags(JsonElement el)
    {
        return new TrendyolRemoteListingFlags(
            Archived: ReadBool(el, "archived"),
            Locked: ReadBool(el, "locked"),
            LockReason: ReadString(el, "lockReason"),
            Blacklisted: ReadBool(el, "blacklisted"),
            BlacklistReason: ReadString(el, "blacklistReason"),
            Rejected: ReadBool(el, "rejected"),
            RejectReason: ReadRejectReason(el),
            HasActiveCampaign: ReadBool(el, "hasActiveCampaign"),
            ProductUrl: ReadString(el, "productUrl"),
            CreatedAtUtc: ReadEpochUtc(el, "createDateTime"),
            UpdatedAtUtc: ReadEpochUtc(el, "lastUpdateDate"));
    }

    // rejectReasonDetails: dizi. Trendyol kimi kayıtta düz metin, kimi kayıtta {reason:...} nesnesi döndürüyor —
    // ikisi de kabul edilir. Birden çok gerekçe tek satıra birleştirilir: kullanıcının gördüğü şey "neden
    // reddedildi" sorusunun cevabıdır, ilk gerekçeyi seçip kalanını atmak eksik cevap olurdu.
    private static string? ReadRejectReason(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object
            || !el.TryGetProperty("rejectReasonDetails", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var reasons = new List<string>();
        foreach (var detail in details.EnumerateArray())
        {
            var text = detail.ValueKind switch
            {
                JsonValueKind.String => detail.GetString(),
                JsonValueKind.Object => ReadString(detail, "reason") ?? ReadString(detail, "reasonName"),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                reasons.Add(text!.Trim());
            }
        }

        return reasons.Count == 0 ? null : string.Join(" · ", reasons);
    }

    // Trendyol zaman damgaları epoch MİLİSANİYE. Kayıt UTC'dir (CLAUDE.md §6: kayıt=UTC, görüntü=yerel).
    private static DateTime? ReadEpochUtc(JsonElement obj, string property)
    {
        return ReadLong(obj, property) is > 0 and { } epochMs
            ? DateTimeOffset.FromUnixTimeMilliseconds(epochMs).UtcDateTime
            : null;
    }

    // ── Create gövdesi (items[]) ──────────────────────────────────────────────────────────────────────

    // internal — gövde ağa çıkmadan birim test edilebilsin diye (BuildPriceInventoryBody emsali;
    // InternalsVisibleTo zaten test derlemesine açık).
    internal static string BuildCreateBody(TrendyolProductData p)
    {
        var images = p.ImageUrls.Select(u => new Dictionary<string, object?> { ["url"] = u }).ToList();

        var items = p.Items.Select(item =>
        {
            // Kalem attribute'ları = ürün-seviyesi + kalemin KENDİ eksen değerleri; aynı attributeId'de
            // kalem KAZANIR (özgül olan geneli yener — eksen niteliği ürün seviyesinde zaten süzülmüş
            // olmalı ama çakışırsa kalemin beyanı doğrudur). Eski davranış tüm kalemlere AYNI listeyi
            // kopyalıyordu → çok varyantlı üründe eksen beyanı push'a hiç girmiyordu.
            var itemAttributes = MergeItemAttributes(p.Attributes, item.Attributes);

            var dict = new Dictionary<string, object?>
            {
                ["barcode"] = item.Barcode,
                ["title"] = p.Title,
                ["productMainId"] = p.ProductMainId,
                ["brandId"] = ParseNumericId(p.BrandId, nameof(p.BrandId)),
                ["categoryId"] = ParseNumericId(p.CategoryId, nameof(p.CategoryId)),
                ["quantity"] = item.Quantity,
                ["stockCode"] = item.StockCode,
                ["description"] = p.Description,
                ["listPrice"] = item.ListPrice,
                ["salePrice"] = item.SalePrice,
                ["vatRate"] = p.VatRate,
                // ZORUNLU (ilk canlı gönderim 2026-08-16'da "productRequest.currencyType.null" ile reddedildi);
                // Trendyol yalnız TRY kabul eder — karışım BuildProductDataAsync'te zaten fail-fast.
                ["currencyType"] = "TRY",
                ["images"] = images,
                ["attributes"] = itemAttributes,
            };

            if (p.CargoCompanyId is { } cargoCompanyId)
            {
                dict["cargoCompanyId"] = cargoCompanyId;
            }

            if (p.DimensionalWeight is { } weight)
            {
                dict["dimensionalWeight"] = weight;
            }

            // deliveryOption: { deliveryDuration, fastDeliveryType } — hızlı teslimat kullanılırsa deliveryDuration=1 zorunlu.
            if (p.DeliveryDuration is { } duration || p.FastDeliveryType is not null)
            {
                var delivery = new Dictionary<string, object?>();
                if (p.DeliveryDuration is { } d)
                {
                    delivery["deliveryDuration"] = d;
                }

                if (p.FastDeliveryType is { } fast)
                {
                    delivery["fastDeliveryType"] = fast == TrendyolFastDeliveryType.SameDayShipping
                        ? "SAME_DAY_SHIPPING"
                        : "FAST_DELIVERY";
                }

                dict["deliveryOption"] = delivery;
            }

            return dict;
        }).ToList();

        var root = new Dictionary<string, object?> { ["items"] = items };
        return JsonSerializer.Serialize(root);
    }

    /// <summary>Ürün-seviyesi + kalem attribute'larını birleştirir; aynı <c>attributeId</c>'de KALEM kazanır.
    /// Kalem listesi boş/null ise sonuç ürün-seviyesinin kendisidir (eski davranışla birebir).</summary>
    private static List<Dictionary<string, object?>> MergeItemAttributes(
        IReadOnlyList<TrendyolAttributeValue> productLevel, IReadOnlyList<TrendyolAttributeValue>? itemLevel)
    {
        if (itemLevel is null || itemLevel.Count == 0)
        {
            return productLevel.Select(BuildAttribute).ToList();
        }

        var itemIds = itemLevel.Select(a => a.AttributeId).ToHashSet();
        return productLevel
            .Where(a => !itemIds.Contains(a.AttributeId))
            .Concat(itemLevel)
            .Select(BuildAttribute)
            .ToList();
    }

    // ── Fiyat/stok gövdesi (items[]) ─────────────────────────────────────────────────────────────────

    /// <summary>Hafif fiyat/stok gövdesini kurar: <c>{ "items": [ { barcode, quantity?, listPrice?, salePrice? } ] }</c>.
    ///
    /// <para><b>public static</b> — ağ olmadan birim test edilebilsin diye (dosyadaki <see cref="FetchAllPagesAsync"/> /
    /// <see cref="ParseSellerProductsPage"/> emsali). Gövdenin şekli bu dilimin tek gerçek riski: yanlış alan adı ya da
    /// yanlış atlama, HTTP 200 dönerken bile yanlış stoğu yazar.</para>
    ///
    /// <para><b>NULL ALAN JSON'A YAZILMAZ.</b> Tabanda ortak <c>JsonSerializerOptions</c> (dolayısıyla
    /// <c>WhenWritingNull</c> garantisi) YOK → atlama ELLE yapılır. <c>null</c> "dokunma", <c>0</c> ise
    /// "sıfırla" demektir ve <c>0</c> YAZILIR — satışı durdurma yolunun taşıyıcısı budur.</para></summary>
    public static string BuildPriceInventoryBody(IReadOnlyList<TrendyolPriceInventoryItem> items)
    {
        if (items is null || items.Count == 0)
        {
            // Boş gövdeyi sessizce POST etmek, "gönderdim" diye görünüp hiçbir şey yapmamaktır.
            throw new BusinessException("TradeXpress:Trendyol:Product:EmptyItems");
        }

        if (items.Count > MaxItemsPerRequest)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:TooManyItems")
                .WithData("count", items.Count)
                .WithData("max", MaxItemsPerRequest);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<Dictionary<string, object?>>(items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];

            if (string.IsNullOrWhiteSpace(item.Barcode))
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:BarcodeRequired").WithData("index", i);
            }

            if (!seen.Add(item.Barcode))
            {
                // Aynı istekte iki kez geçen barkodda hangi satırın kazandığı TANIMSIZ — tahmin etmiyoruz.
                throw Fail("TradeXpress:Trendyol:Product:DuplicateBarcode", item.Barcode);
            }

            if (item.Quantity is null && item.ListPrice is null && item.SalePrice is null)
            {
                // Dört alanın da boş olması "hiçbir şey yapma" demektir; istek kotayı ve 15 dk penceresini
                // boşa harcar. Çağıranın hatasıdır, sessizce geçilmez.
                throw Fail("TradeXpress:Trendyol:Product:NothingToUpdate", item.Barcode);
            }

            if (item.Quantity is < 0)
            {
                throw Fail("TradeXpress:Trendyol:Product:QuantityOutOfRange", item.Barcode);
            }

            if (item.ListPrice is < 0m || item.SalePrice is < 0m)
            {
                throw Fail("TradeXpress:Trendyol:Product:PriceNegative", item.Barcode);
            }

            // Fiyat ÇİFT gönderilir: tek fiyatla gidildiğinde Trendyol'un "listPrice >= salePrice" kuralı
            // UZAKTAKİ eski değere karşı işletilir ve sonuç bizim göremediğimiz bir redde dönüşür.
            if (item.ListPrice is null != item.SalePrice is null)
            {
                throw Fail("TradeXpress:Trendyol:Product:PriceFieldsMustBePaired", item.Barcode);
            }

            if (item.ListPrice is { } listPrice && item.SalePrice is { } salePrice && listPrice < salePrice)
            {
                throw Fail("TradeXpress:Trendyol:Product:ListPriceBelowSalePrice", item.Barcode);
            }

            var dict = new Dictionary<string, object?> { ["barcode"] = item.Barcode };
            if (item.Quantity is { } quantity)
            {
                dict["quantity"] = quantity;
            }

            if (item.ListPrice is { } list)
            {
                dict["listPrice"] = list;
            }

            if (item.SalePrice is { } sale)
            {
                dict["salePrice"] = sale;
            }

            rows.Add(dict);
        }

        var root = new Dictionary<string, object?> { ["items"] = rows };
        return JsonSerializer.Serialize(root);
    }

    /// <summary>Satır hatalarının ortak kurucusu — hangi barkodda patladığı hata verisinde TAŞINIR
    /// (yalnız "geçersiz satır" demek, 1000 satırlık gövdede teşhis ettirmez).</summary>
    private static BusinessException Fail(string code, string barcode)
    {
        return new BusinessException(code).WithData("barcode", barcode);
    }

    // attribute = { attributeId, attributeValueId } YA DA { attributeId, customAttributeValue }.
    private static Dictionary<string, object?> BuildAttribute(TrendyolAttributeValue a)
    {
        var dict = new Dictionary<string, object?> { ["attributeId"] = a.AttributeId };
        if (a.AttributeValueId is { } valueId)
        {
            dict["attributeValueId"] = valueId;
        }
        else if (!string.IsNullOrWhiteSpace(a.CustomValue))
        {
            dict["customAttributeValue"] = a.CustomValue;
        }

        return dict;
    }

    // Trendyol brandId/categoryId numerik bekler — geçersizse dostane hata (kötü yapılandırma, fail-fast).
    private static long ParseNumericId(string value, string field)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:NumericIdInvalid").WithData("field", field);
        }

        return id;
    }

    // ── Yanıt parse ──────────────────────────────────────────────────────────────────────────────────

    private static string? ReadString(string payload, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ReadString(doc.RootElement, property);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement obj, string property)
    {
        if (obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String)
        {
            var value = el.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    /// <summary>Sayısal YA DA metin gelen id'yi string'e indirger (Trendyol id'leri numerik ama matematik değil).</summary>
    private static string? ReadIdAsString(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.String when !string.IsNullOrWhiteSpace(el.GetString()) => el.GetString(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    private static long? ReadLong(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt64(out var l) => l,
            JsonValueKind.String when long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            _ => null,
        };
    }

    private static decimal? ReadDecimal(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDecimal(out var d) => d,
            JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    // { status, itemCount, items:[{ status, failureReasons:[...] }] } → durum + başarısız kalem + gerekçeler.
    /// <summary>Batch durum yanıtını çözer (public static — ağsız birim test edilir).
    ///
    /// <para><b>⚠ KÖK <c>status</c> HER BATCH'TE DÖNMEZ.</b> Trendyol'un resmî notu: fiyat/stok
    /// (<c>price-and-inventory</c>) batch'inde <b>batch seviyesinde <c>status</c> alanı YOKTUR</b>, sonuç
    /// item-bazlı statülerden okunur. Bu yüzden kök alan boşsa durum <b>item statülerinden TÜRETİLİR</b>.</para>
    ///
    /// <para><b>Türetmeseydik ne olurdu:</b> <c>Status</c> hep <c>null</c> döner → finalizasyonun
    /// <c>COMPLETED</c> dalı HİÇ tetiklenmez → <c>LastSent*</c> sonsuza kadar boş kalır → dirty-check her turda
    /// "değişti" der → aynı istek tekrar gider → Trendyol'un 15 dk mükerrer reddine çarpar. Hatasız, logsuz,
    /// sonsuz bir döngü. Kök <c>status</c> DOLU ise ona dokunulmaz — mevcut davranış birebir korunur.</para></summary>
    public static TrendyolBatchStatus ParseBatchStatus(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString()
                : null;
            var itemCount = root.TryGetProperty("itemCount", out var ic) && ic.ValueKind == JsonValueKind.Number
                ? ic.GetInt32()
                : 0;

            var failed = 0;
            var seenItems = 0;
            var reasons = new List<string>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    seenItems++;
                    var itemStatus = item.TryGetProperty("status", out var itemStatusEl) && itemStatusEl.ValueKind == JsonValueKind.String
                        ? itemStatusEl.GetString()
                        : null;
                    if (!string.Equals(itemStatus, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                    {
                        failed++;
                    }

                    if (item.TryGetProperty("failureReasons", out var fr) && fr.ValueKind == JsonValueKind.Array)
                    {
                        reasons.AddRange(fr.EnumerateArray()
                            .Where(r => r.ValueKind == JsonValueKind.String)
                            .Select(r => r.GetString()!)
                            .Where(r => !string.IsNullOrWhiteSpace(r)));
                    }
                }
            }

            var failureReasons = reasons.Count > 0 ? string.Join(" | ", reasons.Distinct()) : null;

            // Kök status YOKSA ama item'lar İŞLENMİŞ olarak dönmüşse batch tamamlanmıştır (bkz. metot doc'u).
            // Koşul bilinçle DAR: yalnız kök alan boş VE en az bir item varken türetilir. items boşsa "işlendi"
            // demek için elimizde kanıt yok — o hâlde null kalır ve kayıt PROCESSING'te bekler (fail-closed).
            if (string.IsNullOrWhiteSpace(status) && seenItems > 0)
            {
                status = "COMPLETED";
            }

            return new TrendyolBatchStatus(status, itemCount, failed, failureReasons);
        }
        catch (JsonException)
        {
            return new TrendyolBatchStatus(null, 0, 0, null);
        }
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500);
    }
}
