using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.TrendyolCategories;

/// <summary>
/// <see cref="ITrendyolCategoryClient"/> — Trendyol V2 REST. Tüm ağaç tek GET'te gelir
/// (<c>/integration/product/product-categories</c> → <c>{ categories: [ { id, name, parentId, subCategories: [...] } ] }</c>);
/// özyinelemeli düzleştirilir (<c>subCategories</c> boş = yaprak). Endpoint public olsa da <see cref="TrendyolRestClientBase"/>
/// üzerinden auth + zorunlu User-Agent eklenir (tutarlılık). Sınıf adı arayüzle eşleştiğinden ABP otomatik expose eder.
/// </summary>
public sealed class TrendyolCategoryClient : TrendyolRestClientBase, ITrendyolCategoryClient, ITransientDependency
{
    private readonly ILogger<TrendyolCategoryClient> _logger;

    public TrendyolCategoryClient(ILogger<TrendyolCategoryClient> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<TrendyolCategoryNode>> GetCategoryTreeAsync(
        TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/product/product-categories";
        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await SendAsync(request, cancellationToken);

        if (!response.Ok)
        {
            _logger.LogWarning("Trendyol kategori ağacı alınamadı (HTTP {Status}).", response.Status);
            throw new BusinessException("TradeXpress:Trendyol:Category:FetchFailed").WithData("status", response.Status);
        }

        var result = new List<TrendyolCategoryNode>();
        using (var doc = JsonDocument.Parse(response.Payload))
        {
            var root = doc.RootElement;
            var array = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("categories", out var c) ? c : root;

            if (array.ValueKind == JsonValueKind.Array)
            {
                foreach (var top in array.EnumerateArray())
                {
                    Flatten(top, null, result);
                }
            }
        }

        if (result.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Category:FetchFailed");
        }

        return result;
    }

    public async Task<TrendyolLeafAttributes> GetLeafAttributesAsync(
        TrendyolCredentials credentials, string categoryExternalId, CancellationToken cancellationToken = default)
    {
        var url = $"{BaseUrl}/integration/product/product-categories/{categoryExternalId}/attributes";
        using var request = CreateRequest(HttpMethod.Get, url, credentials);
        var response = await SendAsync(request, cancellationToken);

        if (!response.Ok)
        {
            _logger.LogWarning(
                "Trendyol kategori attribute'ları alınamadı ({CategoryId}, HTTP {Status}).", categoryExternalId, response.Status);
            throw new BusinessException("TradeXpress:Trendyol:Category:AttributesFetchFailed")
                .WithData("CategoryId", categoryExternalId)
                .WithData("status", response.Status);
        }

        var attrs = new List<TrendyolAttributeDef>();
        using (var doc = JsonDocument.Parse(response.Payload))
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("categoryAttributes", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var ca in list.EnumerateArray())
                {
                    var def = ReadAttribute(ca);
                    if (def is not null)
                    {
                        attrs.Add(def);
                    }
                }
            }
        }

        return new TrendyolLeafAttributes(categoryExternalId, attrs);
    }

    /// <summary>Tek <c>categoryAttributes[]</c> ögesini attribute tanımına indirger (attribute/id yoksa null → atlanır).</summary>
    private static TrendyolAttributeDef? ReadAttribute(JsonElement categoryAttribute)
    {
        if (!categoryAttribute.TryGetProperty("attribute", out var attr) || ReadInt(attr, "id") is not { } attributeId)
        {
            return null;
        }

        var name = attr.TryGetProperty("name", out var an) ? an.GetString() ?? string.Empty : string.Empty;

        var values = new List<TrendyolAttributeValue>();
        if (categoryAttribute.TryGetProperty("attributeValues", out var vlist) && vlist.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in vlist.EnumerateArray())
            {
                if (ReadInt(v, "id") is { } valueId)
                {
                    values.Add(new TrendyolAttributeValue(
                        valueId,
                        v.TryGetProperty("name", out var vn) ? vn.GetString() ?? string.Empty : string.Empty));
                }
            }
        }

        return new TrendyolAttributeDef(
            attributeId,
            name,
            categoryAttribute.TryGetProperty("required", out var rq) && rq.ValueKind == JsonValueKind.True,
            categoryAttribute.TryGetProperty("varianter", out var va) && va.ValueKind == JsonValueKind.True,
            categoryAttribute.TryGetProperty("allowCustom", out var ac) && ac.ValueKind == JsonValueKind.True,
            values);
    }

    /// <summary>Sayısal ya da metin id'yi int'e indirger (çözülemezse null).</summary>
    private static int? ReadInt(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => null,
        };
    }

    /// <summary>Bir düğümü + alt ağacını flat listeye yazar (parentId JSON'da yoksa gezinme parent'ı kullanılır).</summary>
    private static void Flatten(JsonElement node, string? traversalParent, List<TrendyolCategoryNode> acc)
    {
        var id = ReadId(node, "id");
        if (id is null)
        {
            return;
        }

        var parentId = ReadId(node, "parentId") ?? traversalParent;
        var name = node.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var hasChildren = node.TryGetProperty("subCategories", out var sc)
            && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() > 0;

        acc.Add(new TrendyolCategoryNode(id, parentId, name, !hasChildren));

        if (hasChildren)
        {
            foreach (var child in sc.EnumerateArray())
            {
                Flatten(child, id, acc);
            }
        }
    }

    /// <summary>Sayısal ya da metin id'yi string'e indirger; parentId=null (kök) için null döner.</summary>
    private static string? ReadId(JsonElement obj, string prop)
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
}
