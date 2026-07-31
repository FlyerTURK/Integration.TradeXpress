using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Form modelinin İÇ GRAFINI (drill koleksiyonları + değer nesneleri) client tarafında doğrulayıp
/// TAM BAĞLAM YOLLU mesaj üreten gezgin: <c>"Şirket FMS → Şube HQ → Adres: Şehir alanı zorunludur."</c>
///
/// <para><b>Neden var (2026-08-01 denetimi):</b> üst-düzey validator (<c>LocalizedDataAnnotationsValidator</c>)
/// iç grafa İNMEZ; ihlaller sunucuda ABP'nin recursive doğrulayıcısına takılıyor ve kullanıcıya HAM property
/// adlı, bağlamsız İngilizce mesajlar dönüyordu ("Name alanı zorunludur." — hangi kaydın Name'i?). Bu gezgin
/// ihlali sunucuya GİTMEDEN, çevrilmiş alan adı + parent zinciriyle yakalar; ABP doğrulaması SON SAVUNMA kalır.</para>
///
/// <para><b>Kapsam:</b> kökün KENDİ scalar alanları BİLEREK dışarıda — onları üst-düzey validator inline
/// işaretlerle zaten duyurur (çift bildirim olmasın). Gezgin yalnız kompleks/koleksiyon çocuklara iner;
/// soft-delete edilmiş düğümler (<c>IsDeleted=true</c>) atlanır — silinen satırın hatası kullanıcıyı kilitlemesin.</para>
///
/// <para><b>Etiket konvansiyonu:</b> koleksiyon elemanı → entity adı (<c>Entity:X</c> anahtarı; tip adından
/// Dto/GraphDto/Input sonekleri kırpılarak) + kaydın Code/Name'i; tek-nesne çocuk (Adres gibi VO) → property
/// adının çevirisi. Alan adları <see cref="FieldNameLocalizer"/>, mesajlar <see cref="ValidationMessageBuilder"/>
/// üzerinden — üç sunum yolu aynı sözlüğü paylaşır.</para>
/// </summary>
public static class GraphValidator
{
    // Döngü/derinlik emniyeti: meşru graflarımız 4 seviyeyi geçmez (Tenant→Company→Branch→Vault/Address).
    private const int MaxDepth = 6;

    private static readonly string[] TypeNameSuffixes =
        { "GraphDto", "GetDto", "ListDto", "EditDto", "CreateDto", "UpdateDto", "Dto", "Input", "EditModel", "Model" };

    /// <summary>Kök modelin iç grafını doğrular; ihlal başına bağlam-yollu, lokalize TEK satır döner.
    /// Boş liste = graf temiz. Reflection hataları doğrulamayı DÜŞÜRMEZ (fail-open) — sunucu son savunmadır.</summary>
    public static List<string> Validate(object model, IStringLocalizer localizer)
    {
        var errors = new List<string>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        try
        {
            VisitChildren(model, localizer, new List<string>(), errors, visited, depth: 0);
        }
        catch
        {
            // Gezgin asla formu kilitleyemez; bir tip beklenmedik davranırsa kalan doğrulama sunucuya kalır.
        }

        return errors;
    }

    // Kökün ve her düğümün KOMPLEKS çocuklarını gezer (scalar'lar ValidateOwnProperties'te, çocuk düğümlerde).
    private static void VisitChildren(
        object node, IStringLocalizer localizer, List<string> path, List<string> errors, HashSet<object> visited, int depth)
    {
        if (depth >= MaxDepth || !visited.Add(node))
        {
            return;
        }

        var properties = node.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var property in properties)
        {
            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;   // tek property'nin getter'ı patlarsa diğerlerini engelleme
            }

            if (value is null)
            {
                continue;
            }

            if (IsOwnedCollection(value, out var items))
            {
                var index = 0;
                foreach (var item in items)
                {
                    index++;
                    if (item is null || !IsOwnedType(item.GetType()) || IsSoftDeleted(item))
                    {
                        continue;
                    }

                    var label = BuildItemLabel(item, property.Name, index, localizer);
                    VisitNode(item, localizer, Append(path, label), errors, visited, depth + 1);
                }
            }
            else if (IsOwnedType(value.GetType()))
            {
                var label = FieldNameLocalizer.Resolve(localizer, property.Name);
                VisitNode(value, localizer, Append(path, label), errors, visited, depth + 1);
            }
        }
    }

    // Çocuk düğüm: önce kendi scalar attribute'ları (bağlam yoluyla raporlanır), sonra daha derin çocuklar.
    private static void VisitNode(
        object node, IStringLocalizer localizer, List<string> path, List<string> errors, HashSet<object> visited, int depth)
    {
        ValidateOwnProperties(node, localizer, path, errors);
        VisitChildren(node, localizer, path, errors, visited, depth);
    }

    private static void ValidateOwnProperties(object node, IStringLocalizer localizer, List<string> path, List<string> errors)
    {
        var properties = node.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

        foreach (var property in properties)
        {
            var attributes = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToList();
            if (attributes.Count == 0)
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            var context = new ValidationContext(node) { MemberName = property.Name };
            foreach (var attribute in attributes)
            {
                if (attribute.GetValidationResult(value, context) == ValidationResult.Success)
                {
                    continue;
                }

                var displayName = FieldNameLocalizer.Resolve(localizer, property);
                var message = ValidationMessageBuilder.Build(localizer, attribute, displayName);
                errors.Add(path.Count == 0 ? message : $"{string.Join(" → ", path)}: {message}");
            }
        }
    }

    /// <summary>Koleksiyon elemanı etiketi: entity adı + kaydın kendi kimliği (Code → Name → sıra numarası).
    /// Entity adı <c>Entity:X</c> anahtarından (drill başlıklarıyla aynı sözlük); yoksa kırpılmış tip adı denenir.</summary>
    private static string BuildItemLabel(object item, string propertyName, int index, IStringLocalizer localizer)
    {
        var entityLabel = ResolveEntityLabel(item.GetType(), propertyName, localizer);
        var identity = ReadString(item, "Code") ?? ReadString(item, "Name") ?? $"#{index}";
        return $"{entityLabel} {identity}";
    }

    private static string ResolveEntityLabel(Type type, string propertyName, IStringLocalizer localizer)
    {
        var trimmed = type.Name;
        foreach (var suffix in TypeNameSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal) && trimmed.Length > suffix.Length)
            {
                trimmed = trimmed[..^suffix.Length];
                break;
            }
        }

        var entityKey = localizer["Entity:" + trimmed];
        if (!entityKey.ResourceNotFound)
        {
            return entityKey.Value;
        }

        var plain = localizer[trimmed];
        if (!plain.ResourceNotFound)
        {
            return plain.Value;
        }

        return FieldNameLocalizer.Resolve(localizer, propertyName);
    }

    // Yalnız KENDİ DTO'larımıza inilir — framework/BCL tiplerine (string, Guid, List<Guid>...) dalıp
    // anlamsız doğrulama/döngü üretmeyelim. Konvansiyon: tüm sözleşme tipleri "Integration." kökündedir.
    private static bool IsOwnedType(Type type)
    {
        return !type.IsPrimitive
            && !type.IsEnum
            && type.Namespace?.StartsWith("Integration.", StringComparison.Ordinal) == true;
    }

    private static bool IsOwnedCollection(object value, out IEnumerable items)
    {
        if (value is string)
        {
            items = Array.Empty<object>();
            return false;
        }

        if (value is IEnumerable enumerable)
        {
            items = enumerable;
            return true;
        }

        items = Array.Empty<object>();
        return false;
    }

    private static bool IsSoftDeleted(object item)
    {
        var property = item.GetType().GetProperty("IsDeleted", BindingFlags.Public | BindingFlags.Instance);
        return property?.PropertyType == typeof(bool) && property.GetValue(item) is true;
    }

    private static string? ReadString(object item, string propertyName)
    {
        var property = item.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var value = property?.PropertyType == typeof(string) ? property.GetValue(item) as string : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static List<string> Append(List<string> path, string segment)
    {
        var next = new List<string>(path) { segment };
        return next;
    }
}
