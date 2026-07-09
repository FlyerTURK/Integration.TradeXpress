using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.N11Categories;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 push-öncesi kategori-farkındalıklı validasyon (Faz 1) — N11 kuralları: varyant EKSENLERİNİ kategori belirler
/// (isVariant=true attribute seti), satıcı seçemez; customValue=false attribute değeri valueList'ten BİREBİR gelmek
/// zorunda; zorunlu varyant ekseni her SKU'da dolu olmalı. Kurala uymayan push N11'de tanımsız davranış/red üretir
/// → burada FAIL-FAST (lokalize BusinessException). Saf sınıf: ağ/DB yok, birim test edilebilir.
/// </summary>
public sealed class N11ProductPushValidator : ITransientDependency
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Varyant seçeneklerini ve ürün-seviyesi attribute'ları kategori tanımına karşı doğrular.
    /// Dönüş: KANONİK değerlerle (valueList'teki birebir yazımla) temizlenmiş ürün-seviyesi attribute'lar +
    /// varyant-başına seçenek listeleri. Ürün-seviyesinden isVariant=true olanlar FİLTRELENİR (SKU seviyesinde gider).</summary>
    public N11PushValidationResult Validate(
        N11LeafAttributes leaf,
        IReadOnlyList<SalesChannelTrN11ProductCategoryAttribute> productAttributes,
        IReadOnlyList<N11SkuPushCandidate> variants)
    {
        var variantDefs = leaf.Attributes.Where(a => a.IsVariant).ToList();

        // Çok varyantlı ürün + kategoride varyant ekseni yok → SKU'lar N11'de ayırt edilemez.
        if (variants.Count > 1 && variantDefs.Count == 0)
        {
            throw new BusinessException("TradeXpress:N11:Product:CategoryHasNoVariantAxis")
                .WithData("CategoryName", leaf.Name);
        }

        var variantOptions = ValidateVariantOptions(variantDefs, variants, leaf.Name);
        EnsureConsistentAxisSets(variants);
        EnsureUniqueSignatures(variants, variantOptions);

        return new N11PushValidationResult(
            ValidateProductAttributes(leaf, productAttributes),
            variantOptions);
    }

    // ── Varyant (SKU) seçenekleri ───────────────────────────────────────────────────────────────────

    private Dictionary<Guid, List<N11ProductAttributePair>> ValidateVariantOptions(
        List<N11AttributeDef> variantDefs, IReadOnlyList<N11SkuPushCandidate> variants, string categoryName)
    {
        var result = new Dictionary<Guid, List<N11ProductAttributePair>>();

        foreach (var variant in variants)
        {
            var pairs = new List<N11ProductAttributePair>();
            foreach (var attribute in variant.Attributes)
            {
                // Eksen adı kategorinin isVariant setinde olmalı — giyimde "Renk" (variant=false+grouping=true)
                // dahil her sapma net hata: N11'in grup-ürün mekanizmasına SESSİZ bölme yok (Faz 1 kararı).
                var def = variantDefs.FirstOrDefault(d => NameEquals(d.Name, attribute.Name));
                if (def is null)
                {
                    throw new BusinessException("TradeXpress:N11:Product:VariantAxisNotAllowed")
                        .WithData("AttributeName", attribute.Name)
                        .WithData("CategoryName", categoryName);
                }

                pairs.Add(new N11ProductAttributePair(def.Name, CanonicalValue(def, attribute.Value)));
            }

            result[variant.VariantId] = pairs;
        }

        // Zorunlu varyant ekseni HER SKU'da dolu olmalı.
        foreach (var def in variantDefs.Where(d => d.IsMandatory))
        {
            foreach (var variant in variants)
            {
                var value = variant.Attributes.FirstOrDefault(a => NameEquals(a.Name, def.Name))?.Value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new BusinessException("TradeXpress:N11:Product:VariantAxisMissing")
                        .WithData("AttributeName", def.Name)
                        .WithData("VariantCode", variant.VariantCode);
                }
            }
        }

        return result;
    }

    // Aynı üründe SKU'lar arası eksen AD SETİ birebir aynı olmalı — "kimi SKU seçenekli kimi seçeneksiz"
    // (eski boş-liste fallback'i) N11'de tanımsız → fail-fast (Faz 1: rapor Açık Soru A4'ü pratikte kapatır).
    private static void EnsureConsistentAxisSets(IReadOnlyList<N11SkuPushCandidate> variants)
    {
        if (variants.Count <= 1)
        {
            return;
        }

        var axisSets = variants
            .Select(v => string.Join("|", v.Attributes.Select(a => NormalizeName(a.Name)).OrderBy(x => x, StringComparer.Ordinal)))
            .Distinct()
            .ToList();
        if (axisSets.Count > 1)
        {
            throw new BusinessException("TradeXpress:N11:Product:VariantAttributesInconsistent");
        }
    }

    // İki SKU aynı seçenek imzasını taşıyamaz — N11 ayırt edemez, sipariş→varyant çözümü de (attribute imzası) çöker.
    private static void EnsureUniqueSignatures(
        IReadOnlyList<N11SkuPushCandidate> variants, Dictionary<Guid, List<N11ProductAttributePair>> options)
    {
        if (variants.Count <= 1)
        {
            return;
        }

        var collisions = variants
            .GroupBy(v => string.Join(
                "|",
                options[v.VariantId]
                    .Select(p => $"{NormalizeName(p.Name)}={NormalizeName(p.Value)}")
                    .OrderBy(x => x, StringComparer.Ordinal)))
            .FirstOrDefault(g => g.Count() > 1);
        if (collisions is not null)
        {
            throw new BusinessException("TradeXpress:N11:Product:DuplicateVariantSignature")
                .WithData("VariantCodes", string.Join(", ", collisions.Select(v => v.VariantCode)));
        }
    }

    // ── Ürün-seviyesi attribute'lar ─────────────────────────────────────────────────────────────────

    private List<N11ProductAttributePair> ValidateProductAttributes(
        N11LeafAttributes leaf, IReadOnlyList<SalesChannelTrN11ProductCategoryAttribute> productAttributes)
    {
        // Zorunlu ürün-seviyesi (variant OLMAYAN) eksen — ör. Marka — dolu olmalı; N11'e gitmeden fail-fast.
        foreach (var def in leaf.Attributes.Where(d => d.IsMandatory && !d.IsVariant))
        {
            var value = productAttributes.FirstOrDefault(a => NameEquals(a.Name, def.Name))?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BusinessException("TradeXpress:N11:Product:ProductAttributeMissing")
                    .WithData("AttributeName", def.Name);
            }
        }

        var result = new List<N11ProductAttributePair>();
        foreach (var attribute in productAttributes)
        {
            var def = leaf.Attributes.FirstOrDefault(d => NameEquals(d.Name, attribute.Name));

            // Varyant ekseni ürün seviyesinde GÖNDERİLMEZ (SKU'larla gider) — çakışma davranışı tanımsız → filtrele.
            if (def is { IsVariant: true })
            {
                continue;
            }

            if (def is null)
            {
                result.Add(new N11ProductAttributePair(attribute.Name, attribute.Value));   // tanımsız ad: N11 karar versin
                continue;
            }

            result.Add(new N11ProductAttributePair(def.Name, CanonicalValue(def, attribute.Value)));
        }

        return result;
    }

    // ── Değer/ad kuralları ──────────────────────────────────────────────────────────────────────────

    /// <summary>customValue=false + değer listesi olan attribute'ta değer listeden BİREBİR gelmeli; eşleşme
    /// Türkçe-duyarsız yapılır, N11'e listedeki KANONİK yazım gönderilir (ör. "kırmızı" → "Kırmızı").</summary>
    private static string CanonicalValue(N11AttributeDef def, string value)
    {
        var trimmed = value.Trim();
        if (def.IsCustomValue || def.Values.Count == 0)
        {
            return trimmed;
        }

        var match = def.Values.FirstOrDefault(v => NameEquals(v.Value, trimmed));
        if (match is null)
        {
            throw new BusinessException("TradeXpress:N11:Product:AttributeValueNotInList")
                .WithData("AttributeName", def.Name)
                .WithData("AttributeValue", trimmed);
        }

        return match.Value;
    }

    // Türkçe-duyarsız ad/değer karşılaştırması (İ/ı doğru katlanır; "beden" = "Beden").
    private static bool NameEquals(string? left, string? right)
    {
        return string.Compare(left?.Trim(), right?.Trim(), Turkish, CompareOptions.IgnoreCase) == 0;
    }

    private static string NormalizeName(string value)
    {
        return value.Trim().ToUpper(Turkish);
    }
}

/// <summary>Validasyon çıktısı — N11'e gidecek KANONİK ürün-seviyesi attribute'lar (varyant eksenleri filtreli)
/// + varyant-başına kanonik seçenek listeleri.</summary>
public sealed record N11PushValidationResult(
    List<N11ProductAttributePair> ProductAttributes,
    Dictionary<Guid, List<N11ProductAttributePair>> VariantOptions);
