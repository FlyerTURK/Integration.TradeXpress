using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Products;
using Volo.Abp;

namespace Integration.TradeXpress.Channels;

// K10 kişiselleştirme devralma zinciri (PersonalizationValues + ResolvePersonalization) 2026-07-28'de
// KALDIRILDI. Gerekçe: zincirin modellediği şey Etsy'nin tek-kutulu kişiselleştirme bloğuydu ve o blok
// Etsy'de 2026-04-09'da kapandı (gönderen istek hata döner). Yerine gelen çoklu ADLANDIRILMIŞ soru modeli
// bizde SpecialInfo ile ifade ediliyor ve onun devralması zaten liste zinciriyle (ResolveList) çalışıyor —
// ayrı bir blok-anahtarı/alt-alan çözümüne gerek kalmadı.

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
