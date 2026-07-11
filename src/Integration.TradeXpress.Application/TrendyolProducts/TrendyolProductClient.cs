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
/// <para><b>⚠ Create/durum uçları CANLI DOĞRULANMADI</b> (bu oturumda gerçek satıcı kimliği yok); listeleme ucu
/// (<c>GET /integration/product/sellers/{sellerId}/products</c>) kimlik doğrulayıcıda status-probe olarak KANITLI.
/// Gerekirse bu tek dosyadaki sabitleri güncellemek yeterlidir (model/AppService değişmeden):</para>
/// <list type="bullet">
/// <item>Create: <c>POST /integration/product/sellers/{sellerId}/products</c> · gövde <c>{ "items": [ ... ] }</c> → <c>{ "batchRequestId": "..." }</c>.</item>
/// <item>Durum: <c>GET /integration/product/sellers/{sellerId}/products/batch-requests/{batchRequestId}</c>.</item>
/// <item>Listeleme: <c>GET /integration/product/sellers/{sellerId}/products?page=&amp;size=</c> →
/// <c>{ totalElements, totalPages, page, size, content: [ ... ] }</c>.</item>
/// </list>
/// </summary>
public sealed class TrendyolProductClient : TrendyolRestClientBase, ITrendyolProductClient, ITransientDependency
{
    /// <summary>Sayfalama döngüsü güvenlik tavanı — totalPages bozuk/aşırı dönerse sonsuz döngüye girilmez.</summary>
    private const int MaxPageLoops = 500;

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

    public async Task<TrendyolBatchStatus> GetBatchStatusAsync(
        string batchRequestId, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products/batch-requests/{batchRequestId}";

        var request = CreateRequest(HttpMethod.Get, url, credentials);

        var (ok, status, payload) = await SendAsync(request, cancellationToken);
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
        var request = CreateRequest(HttpMethod.Get, url, credentials);

        var (ok, status, payload) = await SendAsync(request, cancellationToken);
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
            Attributes: attributes);

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

    // ── Create gövdesi (items[]) ──────────────────────────────────────────────────────────────────────

    private static string BuildCreateBody(TrendyolProductData p)
    {
        var images = p.ImageUrls.Select(u => new Dictionary<string, object?> { ["url"] = u }).ToList();
        var attributes = p.Attributes.Select(BuildAttribute).ToList();

        var items = p.Items.Select(item =>
        {
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
                ["images"] = images,
                ["attributes"] = attributes,
            };

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
    private static TrendyolBatchStatus ParseBatchStatus(string payload)
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
            var reasons = new List<string>();
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
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
