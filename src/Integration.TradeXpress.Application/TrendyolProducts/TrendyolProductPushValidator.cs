using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Integration.TradeXpress.TrendyolCategories;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Doğrulayıcıya giren aday kalem — ERP varyantı ya da Trendyol-only kombinasyon (kanal özellik
/// grafından çözülmüş ad/değer çiftleriyle; ikisi de aynı doğrulamadan geçer).
/// <para><see cref="PhotoValues"/> = import fotoğrafı (pazaryerinin KENDİ beyanı, id-bazlı) — doluysa eksen
/// KAYNAĞI odur ve kategori-tanımı eşleştirmesine GİRMEZ (kanalın verdiğini kanala geri gönderiyoruz;
/// ad→id türetimi yalnız ERP grafından gelen metin çiftleri için gerekir).</para></summary>
public sealed record TrendyolPushVariantInput(
    Guid CandidateId,
    string Code,
    IReadOnlyList<(string Name, string Value)> ErpOptions,
    IReadOnlyList<SalesChannelTrTrendyolProductSkuRemoteAxisValue> PhotoValues);

/// <summary>Bir kalemin ÇÖZÜLMÜŞ ekseni: push body'sine girecek id-bazlı değerler + PushHistory'ye yazılacak
/// okunur çiftler + yeniden-bağlama imzası (yalnız id'li değerler — serbest metnin valueId'si yoktur).</summary>
public sealed record TrendyolResolvedVariantAxis(
    IReadOnlyList<TrendyolAttributeValue> Attributes,
    IReadOnlyList<(string Name, string Value)> Options,
    IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute> Signature);

/// <summary>Doğrulama sonucu — kanonik ürün-seviyesi attribute listesi (varianter tanıma denk gelenler
/// ELENMİŞ: onlar kalemle gider) + aday başına çözülmüş eksen.</summary>
public sealed record TrendyolPushValidationResult(
    IReadOnlyList<TrendyolAttributeValue> ProductAttributes,
    IReadOnlyDictionary<Guid, TrendyolResolvedVariantAxis> VariantAxes);

/// <summary>
/// TRENDYOL PUSH ÖN-KONTROLÜ — <c>N11ProductPushValidator</c>'ın id-bazlı portu (T6/T8). SAF sınıf: ağ/DB yok.
///
/// <para><b>Neden gerekliydi:</b> gerçek push kategori attribute tanımına HİÇ bakmıyordu — zorunlu attribute
/// eksik, eksen değeri listede yok, iki varyant aynı imzada… hepsi Trendyol'a gidiyor ve saatler sonra batch
/// reddi olarak dönüyordu. N11 bu doğrulamayı baştan kurmuştu; asimetri burada kapanıyor. Tanım alınamazsa push
/// DURUR (çağıran fail-fast'i) — doğrulamasız gönderim yok.</para>
///
/// <para><b>Foto-öncelik:</b> import fotoğrafı (<see cref="SalesChannelTrTrendyolProductSkuRemoteAxisValue"/>)
/// dolu kalemde eksen AYNEN alınır — değerler Trendyol'un kendi beyanıdır, ad→id eşleştirmesi ve liste
/// doğrulaması KONUSUZDUR (kanal kendi id'sini reddedemez; reddederse defter zaten söyler). ERP grafından gelen
/// metin çiftleri ise tanıma karşı çözülür: ad varianter setinde olmalı, değer listede olmalı (AllowCustom ise
/// serbest metin CustomValue olarak geçer) — N11 <c>CanonicalValue</c> felsefesi, kanonik yazım listeden döner.</para>
///
/// <para><b>Karşılaştırma tr-TR + IgnoreCase</b> (İ/i doğru katlansın) — N11 <c>NameEquals</c> ile birebir.</para>
/// </summary>
public sealed class TrendyolProductPushValidator : ITransientDependency
{
    /// <summary>Tam doğrulama: ürün-seviyesi zorunlular + aday eksenleri + tutarlılık/benzersizlik.</summary>
    public TrendyolPushValidationResult Validate(
        IReadOnlyList<TrendyolLeafAttributeDto> leafDefinitions,
        IReadOnlyList<SalesChannelTrTrendyolProductCategoryAttribute> productAttributes,
        IReadOnlyList<TrendyolPushVariantInput> candidates)
    {
        var varianterDefs = leafDefinitions.Where(d => d.Varianter).ToList();

        // Çok kalemli ürün, eksensiz kategoriye SIĞMAZ: Trendyol iki item'ı ancak varianter attribute ile ayırır.
        if (candidates.Count > 1 && varianterDefs.Count == 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:CategoryHasNoVariantAxis");
        }

        // ÜRÜN-SEVİYESİNDE DURAN VARIANTER DEĞERLER KALEME TAŞINIR — elenmez, kaybolmaz. Tek kalemli import
        // eksen çıkaramaz (karşılaştıracak ikinci kalem yok) ve "Renk=Kırmızı"yı ürün seviyesine yazar; kanal
        // özellik grafı da böyle bir değeri kaleme koyamaz. Yalnız ürün seviyesinden ELEMEK (ilk sürüm) bu
        // değeri push'tan tamamen düşürüyordu: zorunlu-varianter kategoride kesin red, opsiyonelde sessiz eksik
        // özellik. Aynı ürünün tek değeri her kalemde aynıdır — çelişki üretmez.
        var inheritedFromProduct = ProductLevelVarianterValues(varianterDefs, productAttributes);

        var axes = new Dictionary<Guid, TrendyolResolvedVariantAxis>();
        foreach (var candidate in candidates)
        {
            axes[candidate.CandidateId] = MergeInherited(ResolveAxis(varianterDefs, candidate), inheritedFromProduct);
        }

        EnsureMandatoryAxes(varianterDefs, candidates, axes);
        EnsureConsistentAxisSets(candidates, axes);
        EnsureUniqueSignatures(candidates, axes);

        return new TrendyolPushValidationResult(
            ValidateProductAttributes(leafDefinitions, productAttributes),
            axes);
    }

    /// <summary>Ürün-seviyesi kayıttaki varianter değerleri (id-bazlı) — kaleme devredilecek küme.</summary>
    private static List<TrendyolResolvedVariantValue> ProductLevelVarianterValues(
        List<TrendyolLeafAttributeDto> varianterDefs,
        IReadOnlyList<SalesChannelTrTrendyolProductCategoryAttribute> productAttributes)
    {
        var result = new List<TrendyolResolvedVariantValue>();
        foreach (var def in varianterDefs)
        {
            var match = productAttributes.FirstOrDefault(a => a.AttributeId == def.AttributeId);
            if (match is null || (match.AttributeValueId is null && string.IsNullOrWhiteSpace(match.CustomValue)))
            {
                continue;
            }

            var valueText = match.AttributeValueId is { } valueId
                ? def.Values.FirstOrDefault(v => v.ValueId == valueId)?.Value ?? valueId.ToString(CultureInfo.InvariantCulture)
                : match.CustomValue!.Trim();
            result.Add(new TrendyolResolvedVariantValue(
                new TrendyolAttributeValue(def.AttributeId, match.AttributeValueId, match.AttributeValueId is null ? match.CustomValue : null),
                (def.Name, valueText),
                match.AttributeValueId is { } id ? new SalesChannelTrTrendyolProductSkuAttribute(def.AttributeId, id) : null));
        }

        return result;
    }

    /// <summary>Kalemin kendi ekseni ÖNCELİKLİDİR; ürün-seviyesinden yalnız kalemde OLMAYAN nitelikler devralınır.</summary>
    private static TrendyolResolvedVariantAxis MergeInherited(
        TrendyolResolvedVariantAxis own, List<TrendyolResolvedVariantValue> inherited)
    {
        var missing = inherited.Where(i => own.Attributes.All(a => a.AttributeId != i.Attribute.AttributeId)).ToList();
        if (missing.Count == 0)
        {
            return own;
        }

        return new TrendyolResolvedVariantAxis(
            own.Attributes.Concat(missing.Select(m => m.Attribute)).ToList(),
            own.Options.Concat(missing.Select(m => m.Option)).ToList(),
            own.Signature.Concat(missing.Where(m => m.SignaturePart is not null).Select(m => m.SignaturePart!)).ToList());
    }

    private sealed record TrendyolResolvedVariantValue(
        TrendyolAttributeValue Attribute,
        (string Name, string Value) Option,
        SalesChannelTrTrendyolProductSkuAttribute? SignaturePart);

    /// <summary>Adayın eksenini çözer — foto varsa AYNEN, yoksa ERP çiftlerinden tanım eşleştirmesiyle.</summary>
    private static TrendyolResolvedVariantAxis ResolveAxis(
        List<TrendyolLeafAttributeDto> varianterDefs, TrendyolPushVariantInput candidate)
    {
        if (candidate.PhotoValues.Count > 0)
        {
            var photoAttributes = candidate.PhotoValues
                .Select(p => new TrendyolAttributeValue(
                    p.AttributeId, p.AttributeValueId, p.AttributeValueId is null ? p.ValueText : null))
                .ToList();
            var photoOptions = candidate.PhotoValues
                .Select(p => (
                    Name: p.AttributeName ?? p.AttributeId.ToString(CultureInfo.InvariantCulture),
                    Value: p.ValueText ?? p.AttributeValueId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty))
                .ToList();
            var photoSignature = candidate.PhotoValues
                .Where(p => p.AttributeValueId is { } valueId)
                .Select(p => new SalesChannelTrTrendyolProductSkuAttribute(p.AttributeId, p.AttributeValueId!.Value))
                .ToList();

            return new TrendyolResolvedVariantAxis(photoAttributes, photoOptions, photoSignature);
        }

        var attributes = new List<TrendyolAttributeValue>();
        var options = new List<(string Name, string Value)>();
        var signature = new List<SalesChannelTrTrendyolProductSkuAttribute>();
        foreach (var (name, value) in candidate.ErpOptions)
        {
            var def = varianterDefs.FirstOrDefault(d => NameEquals(d.Name, name));
            if (def is null)
            {
                // ERP ekseni kategorinin varianter setinde YOK — göndersek Trendyol reddeder; erken ve adresli dur.
                throw new BusinessException("TradeXpress:Trendyol:Product:VariantAxisNotAllowed")
                    .WithData("AttributeName", name);
            }

            var trimmed = value?.Trim() ?? string.Empty;
            var listed = def.Values.FirstOrDefault(v => NameEquals(v.Value, trimmed));
            if (listed is not null)
            {
                // Kanonik yazım LİSTEDEN döner ("kirmizi" → "Kırmızı") — N11 CanonicalValue felsefesi.
                attributes.Add(new TrendyolAttributeValue(def.AttributeId, listed.ValueId, null));
                options.Add((def.Name, listed.Value));
                signature.Add(new SalesChannelTrTrendyolProductSkuAttribute(def.AttributeId, listed.ValueId));
            }
            else if (def.AllowCustom)
            {
                attributes.Add(new TrendyolAttributeValue(def.AttributeId, null, trimmed));
                options.Add((def.Name, trimmed));
            }
            else
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:AttributeValueNotInList")
                    .WithData("AttributeName", def.Name)
                    .WithData("AttributeValue", trimmed);
            }
        }

        return new TrendyolResolvedVariantAxis(attributes, options, signature);
    }

    /// <summary>ZORUNLU varianter tanımlar HER kalemde dolu olmalı — eksik olan Trendyol'da kesin red demektir.
    /// FOTO-kaynaklı aday MUAF: eksen kümesi pazaryerinin KENDİ beyanıdır — kanal o kalemi "Renk"siz
    /// bildirdiyse bizim dayatmamız importlu ürünü bloke ederdi; kanal kendi beyanını reddederse defter söyler.</summary>
    private static void EnsureMandatoryAxes(
        List<TrendyolLeafAttributeDto> varianterDefs,
        IReadOnlyList<TrendyolPushVariantInput> candidates,
        Dictionary<Guid, TrendyolResolvedVariantAxis> axes)
    {
        foreach (var def in varianterDefs.Where(d => d.Required))
        {
            foreach (var candidate in candidates.Where(c => c.PhotoValues.Count == 0))
            {
                if (!axes[candidate.CandidateId].Attributes.Any(a => a.AttributeId == def.AttributeId))
                {
                    throw new BusinessException("TradeXpress:Trendyol:Product:VariantAxisMissing")
                        .WithData("AttributeName", def.Name)
                        .WithData("VariantCode", candidate.Code);
                }
            }
        }
    }

    /// <summary>Tüm kalemler AYNI eksen kümesini taşımalı — biri "Renk" diğeri "Renk+Beden" gönderirse
    /// Trendyol grubu tutarsız sayar.</summary>
    private static void EnsureConsistentAxisSets(
        IReadOnlyList<TrendyolPushVariantInput> candidates, Dictionary<Guid, TrendyolResolvedVariantAxis> axes)
    {
        if (candidates.Count <= 1)
        {
            return;
        }

        var distinctSets = candidates
            .Select(c => string.Join('|', axes[c.CandidateId].Attributes
                .Select(a => a.AttributeId)
                .OrderBy(id => id)))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctSets > 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:VariantAttributesInconsistent");
        }
    }

    /// <summary>İki kalem AYNI eksen imzasını taşıyamaz — aynı "Kırmızı"dan iki barcode, kanalda çakışan
    /// listing üretir (sessiz kabul edilirse biri diğerini ezer).</summary>
    private static void EnsureUniqueSignatures(
        IReadOnlyList<TrendyolPushVariantInput> candidates, Dictionary<Guid, TrendyolResolvedVariantAxis> axes)
    {
        if (candidates.Count <= 1)
        {
            return;
        }

        var clash = candidates
            .GroupBy(c => string.Join('|', axes[c.CandidateId].Attributes
                .OrderBy(a => a.AttributeId)
                .Select(a => a.AttributeId + "=" + (a.AttributeValueId?.ToString(CultureInfo.InvariantCulture)
                    ?? NormalizeValue(a.CustomValue)))), StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (clash is not null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DuplicateVariantSignature")
                .WithData("VariantCodes", string.Join(", ", clash.Select(c => c.Code)));
        }
    }

    /// <summary>Ürün-seviyesi doğrulama: zorunlu (varianter-olmayan) tanımlar dolu olmalı; varianter tanıma
    /// denk gelen ürün-seviyesi attribute ELENİR (o kalemle gider — iki seviyede birden göndermek çelişki
    /// kapısı olurdu).</summary>
    private static List<TrendyolAttributeValue> ValidateProductAttributes(
        IReadOnlyList<TrendyolLeafAttributeDto> leafDefinitions,
        IReadOnlyList<SalesChannelTrTrendyolProductCategoryAttribute> productAttributes)
    {
        var varianterIds = leafDefinitions.Where(d => d.Varianter).Select(d => d.AttributeId).ToHashSet();

        foreach (var def in leafDefinitions.Where(d => d.Required && !d.Varianter))
        {
            var match = productAttributes.FirstOrDefault(a => a.AttributeId == def.AttributeId);
            if (match is null || (match.AttributeValueId is null && string.IsNullOrWhiteSpace(match.CustomValue)))
            {
                throw new BusinessException("TradeXpress:Trendyol:Product:ProductAttributeMissing")
                    .WithData("AttributeName", def.Name);
            }
        }

        return productAttributes
            .Where(a => !varianterIds.Contains(a.AttributeId))
            .Select(a => new TrendyolAttributeValue(a.AttributeId, a.AttributeValueId, a.CustomValue))
            .ToList();
    }

    // tr-TR + IgnoreCase ad/değer eşitliği (İ/i katlanması) — N11 NameEquals ile birebir.
    private static bool NameEquals(string? left, string? right)
    {
        return string.Compare(
            left?.Trim(), right?.Trim(),
            CultureInfo.GetCultureInfo("tr-TR"), CompareOptions.IgnoreCase) == 0;
    }

    private static string NormalizeValue(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpper(CultureInfo.GetCultureInfo("tr-TR"));
    }
}
