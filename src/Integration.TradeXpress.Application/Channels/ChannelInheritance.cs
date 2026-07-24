using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Products;
using Volo.Abp;

namespace Integration.TradeXpress.Channels;

/// <summary>Kişiselleştirme bloğunun anlık görüntüsü — devralma zincirinin İKİ ucu da (ürün varsayılanı ve
/// kanal override'ı) bu biçimde ifade edilir; çözümleyici kanal tipini TANIMAZ (kanal-agnostiklik).
/// Kanal push kodu kendi entity'sinden bu record'u kurar (ör. Etsy: <c>is_personalizable</c> +
/// <c>personalization_instructions</c> + <c>personalization_is_required</c> + <c>personalization_char_count_max</c>).</summary>
public sealed record PersonalizationValues(
    bool IsPersonalizable,
    string? Instructions,
    bool IsRequired,
    int? CharCountMax)
{
    /// <summary>Ürün-seviyesi kişiselleştirme bloğunun anlık görüntüsü — zincirin ÜRÜN ucunun TEK kurucusu (SSOT);
    /// push kodları ürün alanlarını elle kopyalamaz.</summary>
    public static PersonalizationValues Of(Product product)
    {
        return new PersonalizationValues(
            product.IsPersonalizable,
            product.PersonalizationInstructions,
            product.PersonalizationIsRequired,
            product.PersonalizationCharCountMax);
    }
}

/// <summary>Bir ürün eklentisinin push/görüntü anındaki EFEKTİF değerleri — satır override'ı katalogla çözülmüş hâli
/// (<see cref="ChannelInheritance.ResolveAddOns"/> çıktısı). <see cref="AddOnId"/> = katalog referansı
/// (<c>ProductAddOn.AddOnId</c>). Etsy push'unda add-on'lar VARYANT olarak yansıtılır (projeksiyon Faz-2 push işi;
/// bu record yalnız değer-çözüm sonucudur).</summary>
public sealed record EffectiveAddOn(
    Guid AddOnId,
    string Code,
    string Name,
    decimal Price,
    Guid CurrencyUnitId,
    bool IsRequired,
    int DisplayOrder,
    string? Note);

/// <summary>
/// Kanal devralma zinciri — MERKEZÎ çözümleyici (K10 kişiselleştirme + K11 add-on; master plan §1.1.5).
/// Kullanıcı-onaylı kural (2026-07-20): <b>kanal-değeri doluysa kanal, değilse ürün</b> — efektif değer PUSH/GÖRÜNTÜ
/// ANINDA burada birleştirilir, DB'de kopya tutulmaz. Kanal-agnostik: kanal tarafı skaler/record olarak girer,
/// bu sınıf hiçbir kanal entity'sini tanımaz. TradeXpress Application'da yaşar (Framework değil — ürün/kanal
/// kavramları bu modüle özgü).
///
/// <para><b>Bağlanma noktaları:</b> Etsy push (PushToEtsyAsync, Faz-2) HENÜZ YOK — yazıldığında kişiselleştirme
/// için TEK çağrı: <c>ResolvePersonalization(kanalBloğu, PersonalizationValues.Of(product))</c>; add-on→varyant
/// projeksiyonu için girdi: <c>ResolveAddOns(product.AddOns, katalog)</c>. N11/Trendyol kanal-ürünlerinde
/// kişiselleştirme/add-on alanı YOK (TR pazaryerleri desteklemiyor) → o push'lara zincir girmez. Mevcut inline
/// devralmalar (N11 <c>BuildProductDataAsync</c>'teki <c>kanal ?? ürün</c> zincirleri) çalışan push kodudur,
/// bilinçli olarak YERİNDE bırakıldı; yeni kod bu helper'ı kullanır. İSTİSNA — <c>MaxPurchaseQuantity</c>
/// (K4, 2026-07-23): listeleme kuralı çekirdek kargo şablonundan çıkarılıp ürün varsayılanına taşınırken
/// N11 push zinciri <see cref="Resolve{T}"/>'a bağlandı (ilk fiili kullanıcı).</para>
/// </summary>
public static class ChannelInheritance
{
    /// <summary>Skaler zincir: kanal-değeri doluysa kanal, değilse ürün — tek merkez. Fiili kullanıcı:
    /// N11 push <c>MaxPurchaseQuantity</c> devralması (K4, 2026-07-23).</summary>
    public static T? Resolve<T>(T? channelValue, T? productValue) where T : struct
    {
        return channelValue ?? productValue;
    }

    /// <summary>Metin zinciri: kanal-değeri DOLU (boş/whitespace değil) ise kanal, değilse ürün
    /// (TitleOverride/DescriptionOverride "boşsa ürün devralınır" deseninin merkezî hâli).</summary>
    public static string? Resolve(string? channelValue, string? productValue)
    {
        return string.IsNullOrWhiteSpace(channelValue) ? productValue : channelValue;
    }

    /// <summary>Liste zinciri: kanal listesi EN AZ BİR satır içeriyorsa kanal, değilse ürün
    /// (N11 SpecialInfo <c>Count &gt; 0</c> deseninin merkezî hâli). İkisi de boşsa boş liste döner.</summary>
    public static IReadOnlyList<T> ResolveList<T>(IReadOnlyList<T>? channelValues, IReadOnlyList<T>? productValues)
    {
        if (channelValues is { Count: > 0 })
        {
            return channelValues;
        }

        return productValues ?? Array.Empty<T>();
    }

    /// <summary>K10 — kişiselleştirme devralma zinciri. Blok-anahtarı <c>IsPersonalizable</c>:
    /// <list type="bullet">
    /// <item>Kanal bloğu DOLU (<c>channel.IsPersonalizable == true</c>) → kanal bloğu esas; nullable alt alanlar
    /// (<c>Instructions</c>/<c>CharCountMax</c>) boşsa alan-bazında ürüne düşer (diğer alanlarla aynı desen);
    /// <c>IsRequired</c> kanal beyanıyla gider.</item>
    /// <item>Kanal bloğu BOŞ (<c>false</c> — kanal entity setter'ı alt alanları zaten temizler) ya da kanal
    /// kişiselleştirme taşımıyor (<c>null</c>) → ürün bloğu TAMAMEN devralınır.</item>
    /// </list>
    /// BİLİNEN SINIR (migration'sız bilinçli): kanal bool'u üç-durumlu değil → ürün kişiselleştirilebilirken kanal
    /// "açıkça KAPAT" diyemez (false = girilmedi sayılır). İhtiyaç doğarsa <c>bool?</c>'a geçiş AYRI migration
    /// kararıdır (master plan §1.1.5 <c>null=devral</c> ideali).</summary>
    public static PersonalizationValues ResolvePersonalization(
        PersonalizationValues? channel, PersonalizationValues product)
    {
        if (channel is not { IsPersonalizable: true })
        {
            return product;
        }

        return new PersonalizationValues(
            IsPersonalizable: true,
            Instructions: Resolve(channel.Instructions, product.Instructions),
            IsRequired: channel.IsRequired,
            CharCountMax: Resolve(channel.CharCountMax, product.CharCountMax));
    }

    /// <summary>K11 — add-on devralma zinciri. BUGÜN tek kaynak ÜRÜN atamasıdır (hiçbir kanal-ürün entity'sinde
    /// add-on override alanı YOK — keşif 2026-07-23); kanal-override alanı doğduğunda zincir kanal→ürün sırasına
    /// bu imza üzerinden genişler (altyapı ŞİMDİDEN kurulmaz — YAGNI). Satır-düzeyi çözüm:
    /// <c>PriceOverride ?? katalog.Price</c>, <c>CurrencyUnitOverrideId ?? katalog.CurrencyUnitId</c>;
    /// ad/kod katalogdan, zorunluluk/sıra/not atamadan. Katalogda OLMAYAN referans fail-fast'tir (sessiz atlama =
    /// kapsam düşürme — N11 push felsefesiyle aynı): <paramref name="catalog"/> atanan TÜM AddOnId'leri içermeli.</summary>
    public static List<EffectiveAddOn> ResolveAddOns(
        IEnumerable<ProductAddOn> productAddOns,
        IReadOnlyDictionary<Guid, AddOn> catalog)
    {
        var result = new List<EffectiveAddOn>();
        foreach (var assignment in productAddOns.OrderBy(a => a.DisplayOrder))
        {
            if (!catalog.TryGetValue(assignment.AddOnId, out var addOn))
            {
                throw new BusinessException("TradeXpress:Product:AddOnCatalogEntryMissing")
                    .WithData("addOnId", assignment.AddOnId);
            }

            result.Add(new EffectiveAddOn(
                AddOnId: assignment.AddOnId,
                Code: addOn.Code,
                Name: addOn.Name,
                Price: assignment.PriceOverride ?? addOn.Price,
                CurrencyUnitId: assignment.CurrencyUnitOverrideId ?? addOn.CurrencyUnitId,
                IsRequired: assignment.IsRequired,
                DisplayOrder: assignment.DisplayOrder,
                Note: assignment.Note));
        }

        return result;
    }
}
