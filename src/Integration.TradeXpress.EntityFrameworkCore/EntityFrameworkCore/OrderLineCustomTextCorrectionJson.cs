using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Integration.TradeXpress.Orders;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// <see cref="OrderLineOperationalData.CustomTextCorrections"/> owned liste kolonunun (AppOrderLineOperationalData.
/// CustomTextCorrections) JSON serileştirimi — OrderDetailSnapshotJson ile AYNI desen (protected ctor/setter
/// reflection'la açılır; domain'e serialization attribute sızmaz).
/// </summary>
public static class OrderLineCustomTextCorrectionJson
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    public static string Serialize(List<OrderLineCustomTextCorrection>? value)
    {
        return JsonSerializer.Serialize(value ?? new List<OrderLineCustomTextCorrection>(), Options);
    }

    public static List<OrderLineCustomTextCorrection> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<OrderLineCustomTextCorrection>();
        }

        return JsonSerializer.Deserialize<List<OrderLineCustomTextCorrection>>(json, Options)
            ?? new List<OrderLineCustomTextCorrection>();
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(EnableNonPublicMembers);
        return new JsonSerializerOptions { TypeInfoResolver = resolver };
    }

    private static void EnableNonPublicMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(OrderLineCustomTextCorrection))
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
