using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Bir varyantın Trendyol nitelik çifti — <c>attributeId</c> + değer kimliği/metni.</summary>
public sealed record TrendyolVariantAxisValue(
    int AttributeId,
    string AttributeName,
    int? AttributeValueId,
    string ValueText);

/// <summary>
/// Trendyol içe aktarımının VARYANT EKSENİ çözümü.
///
/// <para><b>Ürün seviyesi nitelik</b> = grubun tüm kalemlerinde AYNI değeri taşıyan nitelik.
/// <b>Varyant ekseni</b> = kalemler arasında DEĞERİ DEĞİŞEN nitelik.</para>
/// </summary>
public sealed record TrendyolVariantAxisPlan(
    IReadOnlyList<int> AxisAttributeIds,
    IReadOnlyDictionary<string, IReadOnlyList<TrendyolVariantAxisValue>> ValuesByBarcode,
    IReadOnlyList<TrendyolRemoteAttribute> ProductLevelAttributes);

/// <summary>
/// VARYANT EKSENİNİ İÇE AKTARILAN VERİDEN ÇIKARIR — kategori tanımı çekmeden, eşleme tablosu olmadan.
///
/// <para><b>Neden bu mümkün:</b> Trendyol'un kendi kuralı, aynı <c>productMainId</c> altındaki kalemlerde
/// <i>yalnız <c>attributes</c> bölümünün farklılaşmasını</i> şart koşuyor. Dolayısıyla "kalemler arasında
/// değeri değişen nitelik" ile "varyant ekseni" AYNI ŞEYDİR — tanımın kendisinden çıkar. Kategori
/// tanımlarını (<c>varianter</c> bayrağı) ayrıca çekmek ikinci bir HTTP turu ve ikinci bir gerçek kaynağı
/// olurdu; ikisi çeliştiğinde hangisine inanacağımız da belirsiz kalırdı.</para>
///
/// <para><b>DÜZELTTİĞİ HATA:</b> içe aktarım ürün-seviyesi nitelikleri grubun İLK kaleminden alıyordu. Varyant
/// ekseni varsa bu, birinci varyantın değerini (ör. "50 ml") ÜRÜNÜN değeri sanıp kaydetmek demekti — hem
/// yanlış hem de gönderimde tüm varyantlara aynı değeri yazdıracak bir seed.</para>
///
/// <para><b>Tek kalemli grupta eksen YOKTUR</b> ve bu doğru: karşılaştıracak ikinci kalem olmadığı için
/// hiçbir nitelik "değişen" sayılamaz, üstelik tek varyantta ayırt edici bir eksene ihtiyaç da yok.</para>
///
/// <para><b>Kimliksiz (serbest metin) değerler de eksen olabilir:</b> Trendyol <c>customAttributeValue</c>
/// ile serbest değer kabul ediyor. Karşılaştırma bu yüzden değerin METNİ üzerinden yapılır; kimlik varsa
/// ayrıca taşınır (gönderimde <c>attributeValueId</c> tercih edilir).</para>
/// </summary>
public static class TrendyolVariantAxisResolver
{
    /// <summary>Grubun kalemlerinden ekseni çözer. Kalem yoksa boş plan döner.</summary>
    public static TrendyolVariantAxisPlan Resolve(IReadOnlyList<TrendyolRemoteVariant> variants)
    {
        if (variants.Count == 0)
        {
            return Empty();
        }

        var axisIds = ResolveAxisAttributeIds(variants);

        var valuesByBarcode = new Dictionary<string, IReadOnlyList<TrendyolVariantAxisValue>>(StringComparer.Ordinal);
        foreach (var variant in variants)
        {
            valuesByBarcode[variant.Barcode] = variant.Attributes
                .Where(a => axisIds.Contains(a.AttributeId))
                .Select(a => new TrendyolVariantAxisValue(
                    a.AttributeId,
                    NameOf(a),
                    a.AttributeValueId,
                    ValueTextOf(a)))
                .ToList();
        }

        // Ürün seviyesi = eksen OLMAYANLAR. Kaynak olarak ilk kalem kullanılır ama bu artık güvenlidir:
        // eksen nitelikleri elendiği için geriye kalemler arasında AYNI olan değerler kalır.
        var productLevel = variants[0].Attributes
            .Where(a => !axisIds.Contains(a.AttributeId))
            .ToList();

        return new TrendyolVariantAxisPlan(axisIds.ToList(), valuesByBarcode, productLevel);
    }

    /// <summary>
    /// Değeri kalemler arasında DEĞİŞEN nitelik kimlikleri.
    ///
    /// <para><b>Eksik nitelik de FARKTIR:</b> bir kalemde bulunup diğerinde bulunmayan nitelik eksen sayılır
    /// (yokluk da bir değerdir). Aksi halde "kırmızı" ile "renksiz" aynı kovaya düşerdi.</para>
    /// </summary>
    private static HashSet<int> ResolveAxisAttributeIds(IReadOnlyList<TrendyolRemoteVariant> variants)
    {
        var axis = new HashSet<int>();
        if (variants.Count < 2)
        {
            return axis;
        }

        var allIds = variants.SelectMany(v => v.Attributes.Select(a => a.AttributeId)).Distinct();
        foreach (var attributeId in allIds)
        {
            var distinctValues = variants
                .Select(v => v.Attributes.FirstOrDefault(a => a.AttributeId == attributeId))
                .Select(a => a is null ? null : ValueTextOf(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctValues > 1)
            {
                axis.Add(attributeId);
            }
        }

        return axis;
    }

    /// <summary>Değerin METNİ — kimlikli değerde etiket, serbest değerde girilen metin. Karşılaştırma ve ERP
    /// ekseninin değer adı bunun üzerinden kurulur.</summary>
    private static string ValueTextOf(TrendyolRemoteAttribute attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.AttributeValue))
        {
            return attribute.AttributeValue!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attribute.CustomValue))
        {
            return attribute.CustomValue!.Trim();
        }

        // Ne etiket ne serbest metin geldi: kimliği metne çevirmek, ERP ekseninde okunamaz bir değer üretir
        // ama BOŞ bırakmak iki farklı varyantı aynı kovaya düşürürdü — kimlik en azından AYIRIR.
        return attribute.AttributeValueId is { } valueId
            ? valueId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string NameOf(TrendyolRemoteAttribute attribute)
    {
        return string.IsNullOrWhiteSpace(attribute.AttributeName)
            ? attribute.AttributeId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : attribute.AttributeName!.Trim();
    }

    private static TrendyolVariantAxisPlan Empty()
    {
        return new TrendyolVariantAxisPlan(
            Array.Empty<int>(),
            new Dictionary<string, IReadOnlyList<TrendyolVariantAxisValue>>(StringComparer.Ordinal),
            Array.Empty<TrendyolRemoteAttribute>());
    }
}
