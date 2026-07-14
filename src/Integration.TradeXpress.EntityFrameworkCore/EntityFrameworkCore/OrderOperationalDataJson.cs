using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Integration.TradeXpress.Orders;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// <see cref="OrderOperationalData"/>'nın nullable owned VO kolonlarının (BuyerCorrection/BillingAddressCorrection/
/// ShippingAddressCorrection) JSON serileştirimi — OrderDetailSnapshotJson ile AYNI desen (protected ctor/setter
/// reflection'la açılır; domain'e serialization attribute sızmaz).
/// </summary>
public static class OrderOperationalDataJson
{
    private static readonly HashSet<Type> Types = new()
    {
        typeof(OrderOperationalParty),
        typeof(OrderOperationalAddress),
    };

    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static string? SerializeParty(OrderOperationalParty? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, Options);
    }

    public static OrderOperationalParty? DeserializeParty(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OrderOperationalParty>(json, Options);
    }

    public static string? SerializeAddress(OrderOperationalAddress? value)
    {
        return value is null ? null : JsonSerializer.Serialize(value, Options);
    }

    public static OrderOperationalAddress? DeserializeAddress(string? json)
    {
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OrderOperationalAddress>(json, Options);
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(EnableNonPublicMembers);
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    private static void EnableNonPublicMembers(JsonTypeInfo typeInfo)
    {
        if (!Types.Contains(typeInfo.Type))
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

            var setter = typeInfo.Type.GetProperty(property.Name)?.SetMethod;
            if (setter is not null)
            {
                property.Set = (obj, value) => setter.Invoke(obj, new[] { value });
            }
        }
    }
}
