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
