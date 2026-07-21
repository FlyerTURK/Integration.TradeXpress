using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels.Etsy;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.EtsyTaxonomies;

/// <summary>
/// <see cref="IEtsyTaxonomyClient"/> — Etsy Open API v3 seller-taxonomy OKUMA istemcisi. <c>getSellerTaxonomyNodes</c>
/// tüm ağacı iç içe <c>children[]</c> ile tek GET'te verir; app-level uç → yalnız <c>x-api-key</c> header'ı ister,
/// Bearer token GEREKTİRMEZ. Yanıt <c>results[]</c> özyinelemeli düzleştirilir (children boş = leaf). Defansif JSON
/// okuma (<see cref="EtsyProducts.EtsyProductClient"/> okuyucularıyla aynı toleranslar — alan yoksa/tipi farklıysa null).
/// </summary>
public sealed class EtsyTaxonomyClient : IEtsyTaxonomyClient, ITransientDependency
{
    // Taksonomi çekimi seyrek → paylaşılan tek HttpClient (diğer Etsy istemcileriyle aynı desen).
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<IReadOnlyList<EtsyTaxonomyNode>> GetSellerTaxonomyNodesAsync(
        string apiKeyHeader, CancellationToken cancellationToken = default)
    {
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/seller-taxonomy/nodes";
        var payload = await SendGetAsync(url, apiKeyHeader, cancellationToken);

        var result = new List<EtsyTaxonomyNode>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var top in results.EnumerateArray())
                {
                    Flatten(top, null, result);
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:ParseFailed");
        }

        if (result.Count == 0)
        {
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:FetchFailed");
        }

        return result;
    }

    public async Task<IReadOnlyList<EtsyTaxonomyPropertyResult>> GetPropertiesByTaxonomyIdAsync(
        string apiKeyHeader, long taxonomyId, CancellationToken cancellationToken = default)
    {
        var url = $"{EtsyOAuthConsts.ApiBaseUrl}/application/seller-taxonomy/nodes/{taxonomyId}/properties";
        var payload = await SendGetAsync(url, apiKeyHeader, cancellationToken);

        var result = new List<EtsyTaxonomyPropertyResult>();
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                foreach (var property in results.EnumerateArray())
                {
                    var parsed = ReadProperty(property);
                    if (parsed is not null)
                    {
                        result.Add(parsed);
                    }
                }
            }
        }
        catch (JsonException)
        {
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:ParseFailed");
        }

        // Boş liste MEŞRU (kategoride hiç property tanımlı olmayabilir) → hata değil; jenerik property'ler de dahildir.
        return result;
    }

    /// <summary>Tek bir property JSON düğümünü DTO-öncesi client record'una çevirir (property_id yoksa null → atlanır).</summary>
    private static EtsyTaxonomyPropertyResult? ReadProperty(JsonElement property)
    {
        var propertyId = ReadLong(property, "property_id");
        if (propertyId is null)
        {
            return null;
        }

        var name = ReadString(property, "name") ?? string.Empty;
        var displayName = ReadString(property, "display_name") ?? name;
        var isRequired = ReadBool(property, "is_required") ?? false;
        var supportsVariations = ReadBool(property, "supports_variations") ?? false;
        var isMultivalued = ReadBool(property, "is_multivalued") ?? false;
        var maxValuesAllowed = ReadInt(property, "max_values_allowed");

        var values = new List<EtsyTaxonomyPropertyValue>();
        if (property.TryGetProperty("possible_values", out var possibleValues) && possibleValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in possibleValues.EnumerateArray())
            {
                var valueId = ReadLong(value, "value_id");
                if (valueId is null)
                {
                    continue;
                }

                values.Add(new EtsyTaxonomyPropertyValue(valueId.Value, ReadString(value, "name") ?? string.Empty));
            }
        }

        return new EtsyTaxonomyPropertyResult(
            propertyId.Value, name, displayName, isRequired, supportsVariations, isMultivalued, maxValuesAllowed, values);
    }

    // ── Özyinelemeli düzleştirme (iç içe children[] → flat) ───────────────────────────────────────────

    private static void Flatten(JsonElement node, string? traversalParent, List<EtsyTaxonomyNode> acc)
    {
        var id = ReadId(node, "id");
        if (id is null)
        {
            return;
        }

        var parentId = ReadId(node, "parent_id") ?? traversalParent;
        var name = ReadString(node, "name") ?? string.Empty;
        var level = ReadInt(node, "level") ?? 0;
        var hasChildren = node.TryGetProperty("children", out var children)
            && children.ValueKind == JsonValueKind.Array && children.GetArrayLength() > 0;

        acc.Add(new EtsyTaxonomyNode(id, parentId, name, !hasChildren, level));

        if (hasChildren)
        {
            foreach (var child in children.EnumerateArray())
            {
                Flatten(child, id, acc);
            }
        }
    }

    // ── HTTP ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<string> SendGetAsync(string url, string apiKeyHeader, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // getSellerTaxonomyNodes app-level'dır: yalnız x-api-key (app keystring:secret), Bearer YOK.
        request.Headers.TryAddWithoutValidation("x-api-key", apiKeyHeader);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:Etsy:Taxonomy:FetchFailed", $"HTTP {(int)response.StatusCode}: {Truncate(payload)}")
                .WithData("status", (int)response.StatusCode)
                .WithData("body", Truncate(payload));
        }

        return payload;
    }

    // ── Defansif JSON okuyucular (EtsyProductClient ile aynı toleranslar) ─────────────────────────────

    private static string? ReadId(JsonElement obj, string property)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.String => el.GetString(),
            _ => null,
        };
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
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null,
        };
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500);
    }
}
