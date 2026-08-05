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
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Integration.TradeXpress.N11Products;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// <see cref="IN11CategoryClient"/> — REST <c>/cdn</c> primary, SOAP CategoryService fallback. REST tüm ağacı tek
/// GET'te + valueId verir (keşif: findings.md). REST hata verirse SOAP'a düşer (tree = BFS walk, YAVAŞ ama son çare;
/// attribute = tek çağrı). Auth: REST header <c>appkey/appsecret</c>, SOAP gövde <c>&lt;auth&gt;</c> (+ header hedge).
/// Sınıf adı arayüzle eşleştiği için ABP otomatik expose eder. Sir/secret ASLA loglanmaz.
/// </summary>
public sealed class N11CategoryClient : IN11CategoryClient, ITransientDependency
{
    private const string SoapSchemaNs = "http://www.n11.com/ws/schemas";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<N11CategoryClient> _logger;

    // Uç adresleri N11EndpointOptions'tan gelir (varsayılan https://api.n11.com). Sabit adres, istekleri
    // yerel bir sahte sunucuya yönlendirmeyi imkânsız kılıyordu — hesap kapalıyken denemenin tek yolu bu.
    private readonly N11EndpointOptions _endpoints;

    private string RestBase
    {
        get { return _endpoints.RestCdnBase; }
    }

    private string SoapEndpoint
    {
        get { return _endpoints.CategoryServiceEndpoint; }
    }

    public N11CategoryClient(ILogger<N11CategoryClient> logger, IOptions<N11EndpointOptions> endpointOptions)
    {
        _logger = logger;
        _endpoints = endpointOptions.Value;
    }

    // ── Kategori ağacı ──────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<N11CategoryNode>> GetCategoryTreeAsync(
        string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetTreeViaRestAsync(appKey, appSecret, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "N11 kategori ağacı REST'ten alınamadı → SOAP fallback (yavaş).");
            return await GetTreeViaSoapAsync(appKey, appSecret, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<N11CategoryNode>> GetTreeViaRestAsync(
        string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var body = await RestGetAsync($"{RestBase}/categories", appKey, appSecret, cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("categories", out var c) ? c : root;

        var result = new List<N11CategoryNode>();
        if (array.ValueKind == JsonValueKind.Array)
        {
            foreach (var top in array.EnumerateArray())
            {
                FlattenRest(top, null, result);
            }
        }

        if (result.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:CategoryFetchFailed");
        }

        return result;
    }

    private static void FlattenRest(JsonElement node, string? traversalParent, List<N11CategoryNode> acc)
    {
        var id = ReadJsonId(node, "id");
        if (id is null)
        {
            return;
        }

        var parentId = ReadJsonId(node, "parentId") ?? traversalParent;
        var name = node.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var hasChildren = node.TryGetProperty("subCategories", out var sc)
            && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() > 0;

        acc.Add(new N11CategoryNode(id, parentId, name, !hasChildren, null));

        if (hasChildren)
        {
            foreach (var child in sc.EnumerateArray())
            {
                FlattenRest(child, id, acc);
            }
        }
    }

    // ── Yaprak kategori attribute'ları ──────────────────────────────────────────────────────────────

    public async Task<N11LeafAttributes> GetLeafAttributesAsync(
        string categoryExternalId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetAttributesViaRestAsync(categoryExternalId, appKey, appSecret, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "N11 attribute REST'ten alınamadı ({CategoryId}) → SOAP fallback.", categoryExternalId);
            return await GetAttributesViaSoapAsync(categoryExternalId, appKey, appSecret, cancellationToken);
        }
    }

    private async Task<N11LeafAttributes> GetAttributesViaRestAsync(
        string categoryExternalId, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var body = await RestGetAsync($"{RestBase}/category/{categoryExternalId}/attribute", appKey, appSecret, cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var attrs = new List<N11AttributeDef>();

        if (root.TryGetProperty("categoryAttributes", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in list.EnumerateArray())
            {
                var values = new List<N11AttributeValue>();
                if (a.TryGetProperty("attributeValues", out var vlist) && vlist.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in vlist.EnumerateArray())
                    {
                        values.Add(new N11AttributeValue(
                            ReadJsonId(v, "id"),
                            v.TryGetProperty("value", out var vv) ? vv.GetString() ?? string.Empty : string.Empty));
                    }
                }

                attrs.Add(new N11AttributeDef(
                    ReadJsonId(a, "attributeId") ?? string.Empty,
                    a.TryGetProperty("attributeName", out var an) ? an.GetString() ?? string.Empty : string.Empty,
                    a.TryGetProperty("isMandatory", out var m) && m.ValueKind == JsonValueKind.True,
                    a.TryGetProperty("isVariant", out var iv) && iv.ValueKind == JsonValueKind.True,
                    a.TryGetProperty("isCustomValue", out var cv) && cv.ValueKind == JsonValueKind.True,
                    a.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDouble() : null,
                    values));
            }
        }

        return new N11LeafAttributes(categoryExternalId, name, attrs);
    }

    // ── REST GET yardımcısı ─────────────────────────────────────────────────────────────────────────

    private static async Task<string> RestGetAsync(string url, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.TryAddWithoutValidation("appsecret", appSecret);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"N11 REST {url} → HTTP {(int)response.StatusCode}");
        }

        return body;
    }

    // ── SOAP fallback ───────────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<N11CategoryNode>> GetTreeViaSoapAsync(
        string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var result = new List<N11CategoryNode>();
        var top = await SoapCategoriesAsync("GetTopLevelCategories", null, appKey, appSecret, cancellationToken);
        var queue = new Queue<(string Id, string Name, DateTime? Mod)>(top);
        foreach (var t in top)
        {
            result.Add(new N11CategoryNode(t.Id, null, t.Name, false, t.Mod));   // yaprak durumu alt-sorguda netleşir
        }

        var indexById = new Dictionary<string, int>();
        for (var i = 0; i < result.Count; i++)
        {
            indexById[result[i].ExternalId] = i;
        }

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parent = queue.Dequeue();
            var subs = await SoapCategoriesAsync("GetSubCategories", parent.Id, appKey, appSecret, cancellationToken);
            if (subs.Count == 0)
            {
                continue;   // yaprak (varsayılan zaten IsLeaf=... aşağıda düzeltilir)
            }

            // parent artık yaprak DEĞİL
            if (indexById.TryGetValue(parent.Id, out var pi))
            {
                result[pi] = result[pi] with { IsLeaf = false };
            }

            foreach (var s in subs)
            {
                if (!indexById.ContainsKey(s.Id))
                {
                    indexById[s.Id] = result.Count;
                    result.Add(new N11CategoryNode(s.Id, parent.Id, s.Name, true, s.Mod));   // varsayılan yaprak; çocuk çıkarsa düzelir
                    queue.Enqueue(s);
                }
            }
        }

        return result;
    }

    private async Task<N11LeafAttributes> GetAttributesViaSoapAsync(
        string categoryExternalId, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var inner = $"<categoryId>{categoryExternalId}</categoryId><pagingData><currentPage>0</currentPage><pageSize>1000</pageSize></pagingData>";
        var xml = await SoapAsync("GetCategoryAttributes", inner, appKey, appSecret, cancellationToken);
        var doc = XDocument.Parse(xml);

        var attrs = new List<N11AttributeDef>();
        foreach (var a in doc.Descendants().Where(e => e.Name.LocalName == "attribute"))
        {
            var values = a.Descendants()
                .Where(e => e.Name.LocalName == "value")
                .Select(v => new N11AttributeValue(null, LocalValue(v, "name") ?? v.Value.Trim()))
                .ToList();

            attrs.Add(new N11AttributeDef(
                LocalValue(a, "id") ?? string.Empty,
                LocalValue(a, "name") ?? string.Empty,
                string.Equals(LocalValue(a, "mandatory"), "true", StringComparison.OrdinalIgnoreCase),
                string.Equals(LocalValue(a, "variant"), "true", StringComparison.OrdinalIgnoreCase),
                string.Equals(LocalValue(a, "customValue"), "true", StringComparison.OrdinalIgnoreCase),
                double.TryParse(LocalValue(a, "priority"), NumberStyles.Any, CultureInfo.InvariantCulture, out var priority) ? priority : null,   // WSDL: xs:double
                values));
        }

        return new N11LeafAttributes(categoryExternalId, string.Empty, attrs);
    }

    /// <summary>GetTopLevelCategories / GetSubCategories → (id, name, lastModifiedDate) listesi (namespace-agnostik).</summary>
    private async Task<List<(string Id, string Name, DateTime? Mod)>> SoapCategoriesAsync(
        string op, string? categoryId, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var inner = categoryId is null ? string.Empty : $"<categoryId>{categoryId}</categoryId>";
        var xml = await SoapAsync(op, inner, appKey, appSecret, cancellationToken);
        var doc = XDocument.Parse(xml);

        // GetSubCategories → subCategory[]; GetTopLevelCategories → category[] (kök category dışında)
        var nodes = doc.Descendants().Where(e => e.Name.LocalName is "subCategory" or "category").ToList();
        var list = new List<(string, string, DateTime?)>();
        foreach (var e in nodes)
        {
            var id = LocalValue(e, "id");
            if (id is null)
            {
                continue;
            }

            // GetSubCategories yanıtındaki KÖK <category> (sorgulanan parent) atlanır — yalnız subCategory çocukları
            if (e.Name.LocalName == "category" && categoryId is not null && id == categoryId)
            {
                continue;
            }

            list.Add((id, LocalValue(e, "name") ?? string.Empty, ParseN11Date(LocalValue(e, "lastModifiedDate"))));
        }

        return list;
    }

    private async Task<string> SoapAsync(string op, string inner, string appKey, string appSecret, CancellationToken cancellationToken)
    {
        var envelope =
            $"<soapenv:Envelope xmlns:soapenv=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:sch=\"{SoapSchemaNs}\">" +
            $"<soapenv:Header/><soapenv:Body><sch:{op}Request>" +
            $"<auth><appKey>{appKey}</appKey><appSecret>{appSecret}</appSecret></auth>{inner}" +
            $"</sch:{op}Request></soapenv:Body></soapenv:Envelope>";

        using var content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        content.Headers.TryAddWithoutValidation("SOAPAction", "\"\"");
        using var request = new HttpRequestMessage(HttpMethod.Post, SoapEndpoint) { Content = content };
        request.Headers.TryAddWithoutValidation("appkey", appKey);
        request.Headers.TryAddWithoutValidation("appsecret", appSecret);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"N11 SOAP {op} → HTTP {(int)response.StatusCode}");
        }

        return body;
    }

    // ── Küçük yardımcılar ───────────────────────────────────────────────────────────────────────────

    private static string? ReadJsonId(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.ToString(),
            JsonValueKind.String => v.GetString(),
            _ => null,
        };
    }

    private static string? LocalValue(XElement parent, string localName)
    {
        var child = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        return child?.Value.Trim();
    }

    private static DateTime? ParseN11Date(string? raw)
    {
        // N11 SOAP tarih formatı: "dd/MM/yyyy HH:mm"
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParseExact(raw, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}
