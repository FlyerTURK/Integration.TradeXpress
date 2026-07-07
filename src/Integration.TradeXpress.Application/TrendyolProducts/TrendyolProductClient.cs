using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="ITrendyolProductClient"/> — Trendyol Marketplace API v2 (REST/JSON). Basic auth (apiKey:apiSecret) +
/// User-Agent "{sellerId} - SelfIntegration". Ürün oluşturma ASENKRON (submit → batchRequestId; durum ayrı sorgu).
///
/// <para><b>⚠ CANLI DOĞRULANMADI.</b> Aşağıdaki wire varsayımları Trendyol'un kamuya açık entegrasyon dokümanına
/// dayanır; bu oturumda gerçek satıcı kimliği olmadığından TEST EDİLMEMİŞTİR. Kullanıcı gerçek kimlikle doğrulamalı;
/// gerekirse bu tek dosyadaki sabitleri güncellemek yeterlidir (model/AppService değişmeden):</para>
/// <list type="bullet">
/// <item>Base: <c>https://apigw.trendyol.com</c> (eski geçit <c>api.trendyol.com/sapigw</c> ise <see cref="BaseUrl"/> değiştir).</item>
/// <item>Create: <c>POST /integration/product/sellers/{sellerId}/products</c> · gövde <c>{ "items": [ ... ] }</c> → <c>{ "batchRequestId": "..." }</c>.</item>
/// <item>Durum: <c>GET /integration/product/sellers/{sellerId}/products/batch-requests/{batchRequestId}</c>.</item>
/// <item>Item alanları: barcode, title, productMainId, brandId, categoryId, quantity, stockCode, dimensionalWeight,
/// description, currencyType, listPrice, salePrice, vatRate, cargoCompanyId, images[{url}], attributes[{attributeId, attributeValueId|customAttributeValue}].</item>
/// </list>
/// </summary>
public sealed class TrendyolProductClient : ITrendyolProductClient, ITransientDependency
{
    private const string BaseUrl = "https://apigw.trendyol.com";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<TrendyolSubmitResult> SubmitProductAsync(
        TrendyolProductData product, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        var body = BuildCreateBody(product);
        var url = $"{BaseUrl}/integration/product/sellers/{credentials.SellerId}/products";

        using var request = CreateRequest(HttpMethod.Post, url, credentials);
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

        using var request = CreateRequest(HttpMethod.Get, url, credentials);

        var (ok, status, payload) = await SendAsync(request, cancellationToken);
        if (!ok)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:StatusFailed")
                .WithData("status", status)
                .WithData("body", Truncate(payload));
        }

        return ParseBatchStatus(payload);
    }

    // ── HTTP ────────────────────────────────────────────────────────────────────────────────────────

    // Basic auth (apiKey:apiSecret) + Trendyol'un beklediği User-Agent "{sellerId} - SelfIntegration".
    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, TrendyolCredentials credentials)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.UserAgent.ParseAdd($"{credentials.SellerId} - SelfIntegration");
        return request;
    }

    private static async Task<(bool Ok, int Status, string Payload)> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return ((int)response.StatusCode is >= 200 and < 300, (int)response.StatusCode, payload);
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
                ["currencyType"] = item.CurrencyType,
                ["listPrice"] = item.ListPrice,
                ["salePrice"] = item.SalePrice,
                ["vatRate"] = p.VatRate,
                ["images"] = images,
                ["attributes"] = attributes,
            };

            if (p.CargoCompanyId is { } cargo)
            {
                dict["cargoCompanyId"] = cargo;
            }

            if (p.DimensionalWeight is { } weight)
            {
                dict["dimensionalWeight"] = weight;
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
            return doc.RootElement.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
