using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Gelen nitelik grafını kategoriye MERGE eder — <b>kimlikler korunarak</b>.
///
/// <para><b>Neden replace değil merge:</b> nitelik ve değer kimlikleri pazaryeri eşleştirmesinin hedefidir
/// ("bu nitelik N11'de şu attributeId"). Güncellemede listeyi baştan kurmak her kaydetmede yeni Id üretir ve
/// tüm eşleştirmeleri sessizce koparırdı.</para>
///
/// <para><b>DEVRALINAN satırlar (2026-07-28 Hakan):</b> grid üst kategorilerin niteliklerini de gösterir, ama
/// onlar BU kategoriye ait değildir — sahibinde düzenlenirler. Bu yüzden kaydetmede devralınan satırın kendisi
/// yok sayılır. Tek istisna: kullanıcı devralınan bir niteliğin altına KENDİ değerini eklediyse, o kategoride
/// aynı adlı bir nitelik açılır ve YALNIZ kendi değerleri ona yazılır. Kalıtım birleştirmesi ikisini yine tek
/// nitelik olarak gösterdiğinden kullanıcı bu ayrıntıyı hiç görmez.</para>
///
/// <para>AppService'ten AYRI ve <c>static</c>: kural DB'siz sınanabilsin (bu sınıfın testleri, kimlik korunumu
/// ya da devralınan-koruması bozulduğunda kırmızı yanar).</para>
/// </summary>
public static class ProductCategoryAttributeMerger
{
    /// <summary>
    /// Nitelikleri uygular: <c>Id</c>'si eşleşen satır GÜNCELLENİR, gelmeyen satır SİLİNİR, boş <c>Id</c> YENİ
    /// satırdır. Adı boş olan ve KENDİ katkısı olmayan devralınan satırlar elenir.
    /// </summary>
    public static void Apply(ProductCategory category, IReadOnlyList<ProductCategoryAttributeDto>? attributes)
    {
        var incoming = (attributes ?? Array.Empty<ProductCategoryAttributeDto>())
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Where(HasOwnContribution)
            .ToList();

        EnsureNoDuplicate(
            incoming.Select(a => a.Name),
            "TradeXpress:ProductCategory:AttributeNameAlreadyExists",
            "name");

        category.RemoveAttributesExcept(KeepIds(incoming.Select(a => a.Id)));

        foreach (var dto in incoming)
        {
            // Devralınan satırın Id'si ÜST kategoriye aittir → asla eşleştirme anahtarı olamaz. Kendi değeri
            // eklendiği için buraya geldiyse, bu kategoride aynı adlı YENİ bir nitelik açılır.
            var attribute = dto.IsInherited || dto.Id == Guid.Empty ? null : category.FindAttribute(dto.Id);
            if (attribute is null)
            {
                // Id gelmiş ama bu kategoride yoksa: başka kategorinin satırını buraya ÇEKMEK yerine yeni satır
                // açılır — id göndererek başkasının niteliğini ele geçirme yolu kapalı.
                attribute = category.AddAttribute(dto.Name, dto.Kind, dto.DisplayOrder);
            }
            else
            {
                attribute.SetName(dto.Name);
                attribute.SetKind(dto.Kind);
                attribute.SetDisplayOrder(dto.DisplayOrder);
            }

            ApplyValues(attribute, dto.Values);
        }
    }

    /// <summary>
    /// Satır kaydedilmeye değer mi: KENDİ niteliği daima kaydedilir; devralınan yalnız altına KENDİ değeri
    /// eklenmişse kaydedilir (o zaman "gölge nitelik" olarak bu kategoride açılır). Salt devralınan satır
    /// kaydedilirse üst kategorinin nitelikleri her alt kategoriye kopyalanır ve kalıtım anlamsızlaşırdı.
    /// </summary>
    private static bool HasOwnContribution(ProductCategoryAttributeDto attribute)
    {
        if (!attribute.IsInherited)
        {
            return true;
        }

        return (attribute.Values ?? new List<ProductCategoryAttributeValueDto>())
            .Any(v => !v.IsInherited && !string.IsNullOrWhiteSpace(v.Value));
    }

    private static void ApplyValues(ProductCategoryAttribute attribute, IReadOnlyList<ProductCategoryAttributeValueDto>? values)
    {
        // DEVRALINAN değerler atlanır: onlar üst kategoride yaşar. Buraya kopyalanırlarsa aynı değer iki
        // kategoride birden durur ve üstteki düzeltildiğinde alttaki bayat kalırdı.
        var incoming = (values ?? Array.Empty<ProductCategoryAttributeValueDto>())
            .Where(v => !v.IsInherited && !string.IsNullOrWhiteSpace(v.Value))
            .ToList();

        EnsureNoDuplicate(
            incoming.Select(v => v.Value),
            "TradeXpress:ProductCategory:AttributeValueAlreadyExists",
            "value");

        attribute.RemoveValuesExcept(KeepIds(incoming.Select(v => v.Id)));

        foreach (var dto in incoming)
        {
            var value = dto.Id == Guid.Empty ? null : attribute.FindValue(dto.Id);
            if (value is null)
            {
                attribute.AddValue(dto.Value, dto.DisplayOrder);
            }
            else
            {
                value.SetValue(dto.Value);
                value.SetDisplayOrder(dto.DisplayOrder);
            }
        }
    }

    private static HashSet<Guid> KeepIds(IEnumerable<Guid> ids)
    {
        return ids.Where(id => id != Guid.Empty).ToHashSet();
    }

    /// <summary>
    /// Aynı gönderimde yinelenen ad/değer olmadığını doğrular — DB'de <c>(CategoryId, Name)</c> ve
    /// <c>(AttributeId, Value)</c> üzerinde UNIQUE index var; ön-kontrol olmadan kullanıcı bunu ham SQL
    /// çakışması (anlaşılmaz genel hata) olarak görürdü.
    ///
    /// <para>Karşılaştırma BÜYÜK/küçük harf duyarSIZ: kalıtım birleştirmesi de nitelikleri
    /// <c>OrdinalIgnoreCase</c> ile eşleştirir (<see cref="ProductCategoryTreeManager.MergeAttributes"/>),
    /// dolayısıyla "Renk" ile "RENK" sistemde zaten TEK niteliktir.</para>
    /// </summary>
    private static void EnsureNoDuplicate(IEnumerable<string> texts, string errorCode, string dataKey)
    {
        var duplicate = texts
            .Select(t => t.Trim())
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate is not null)
        {
            throw new BusinessException(errorCode).WithData(dataKey, duplicate.Key);
        }
    }
}
