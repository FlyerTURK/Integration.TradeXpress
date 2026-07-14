using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Integration.TradeXpress.Orders;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// <see cref="OrderDetailSnapshot"/> owned VO ağacının <b>TEK JSON kolonu</b> (AppOrders.Detail) serileştirimi.
/// SideCostSettingsJson ile AYNI gerekçe/desen: EF native <c>ToJson()</c> yerine System.Text.Json value-converter —
/// derin iç içe owned yapı (buyer/adres/totals/items→attributes) tek nvarchar(max)'ta yaşar, alt-owned mapping
/// karmaşası yok. VO'ların protected ctor/setter'ları contract-modifier'la reflection üzerinden AÇILIR (domain'e
/// serialization attribute SIZMAZ; encapsulation korunur). Tolerant/kırpma guard'ları VO ctor'unda zaten çalışmıştır.
/// </summary>
public static class OrderDetailSnapshotJson
{
    // Snapshot ağacındaki TÜM VO tipleri — non-public ctor/setter'ları bunlar için açılır.
    private static readonly HashSet<Type> SnapshotTypes = new()
    {
        typeof(OrderDetailSnapshot),
        typeof(OrderDetailParty),
        typeof(OrderDetailAddress),
        typeof(OrderDetailTotals),
        typeof(OrderDetailItem),
        typeof(OrderDetailItemAttribute),
        typeof(OrderDetailItemCustomText),
    };

    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static string? Serialize(OrderDetailSnapshot? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, Options);
    }

    public static OrderDetailSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OrderDetailSnapshot>(json, Options);
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(EnableNonPublicMembers);
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    // Snapshot VO tipleri için: protected parametresiz ctor + protected setter'ları reflection'la aç.
    private static void EnableNonPublicMembers(JsonTypeInfo typeInfo)
    {
        if (!SnapshotTypes.Contains(typeInfo.Type))
        {
            return;
        }

        typeInfo.CreateObject = () => Activator.CreateInstance(typeInfo.Type, nonPublic: true)!;

        foreach (var property in typeInfo.Properties)
        {
            if (property.Set is not null)
            {
                continue;
            }

            // JSON adı = CLR adı (PascalCase; naming policy yok) → doğrudan property lookup güvenli.
            var setter = typeInfo.Type.GetProperty(property.Name)?.SetMethod;
            if (setter is not null)
            {
                property.Set = (obj, value) => setter.Invoke(obj, new[] { value });
            }
        }
    }
}
