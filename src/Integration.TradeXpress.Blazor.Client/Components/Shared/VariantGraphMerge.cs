using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>
/// Otomatik varyant senkronu için MERGE — taze üretilen kartezyen ile MEVCUT varyant listesini birleştirir:
/// var olan kombinasyon (CombinationKey imzasıyla eşleşen) KORUNUR (kullanıcının barkod/GTIN/fiyat/görsel düzenlemeleri +
/// Id + entity-özel uzantı alanları), yalnız türetilen alanlar (Kod/Ad/Özet/IsMain) tazelenir; yeni kombinasyon eklenir;
/// geçersiz kombinasyon düşer. Böylece nitelik/değer değişiminde "Varyant Oluştur" butonuna bağlı kalmadan varyantlar
/// VERİ KAYBETMEDEN yeniden düzenlenir. Niteliksiz (üretilen boş) → tek ANA varyanta iner (mevcut main'in düzenlemesi korunur).
/// </summary>
public static class VariantGraphMerge
{
    public static void Apply<TVariant>(List<TVariant> existing, IReadOnlyList<TVariant> generated)
        where TVariant : EntityVariantGraphDto, new()
    {
        // Niteliksiz → tek ANA varyant (mevcut main/ilk kaydın düzenlemesi korunur, base main kimliğine döner).
        if (generated.Count == 0)
        {
            CollapseToMain(existing);
            return;
        }

        // Var olanları CombinationKey ile indeksle (yüklenenler LoadGraphAsync'te + oturumda üretilenler artık key taşır).
        var byKey = existing
            .Where(e => !string.IsNullOrEmpty(e.CombinationKey))
            .GroupBy(e => e.CombinationKey)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<TVariant>(generated.Count);
        foreach (var g in generated)
        {
            if (byKey.TryGetValue(g.CombinationKey, out var kept))
            {
                // Eşleşti → mevcut KORUNUR (kullanıcı düzenlemeleri), yalnız türetilen alanlar tazelenir.
                kept.IsMain = g.IsMain;
                kept.Code = g.Code;
                kept.Name = g.Name;
                kept.AttributeSummary = g.AttributeSummary;
                result.Add(kept);
            }
            else
            {
                // Yeni kombinasyon → taze üretilmiş satır (host entity-özel default'larını — ör. Good para birimi — zaten set etti).
                result.Add(g);
            }
        }

        existing.Clear();
        existing.AddRange(result);
    }

    /// <summary>Değeri olmayan (silinmemiş) nitelik VAR MI — varsa kartezyen tanımsız (kullanıcı hâlâ değer ekliyor) →
    /// otomatik regen ATLANMALI (transient hal; toast/veri değişikliği yok). Zero-nitelik → false (üretim boş → base main'e iner).</summary>
    public static bool HasIncompleteAttribute(IEnumerable<EntityAttributeGraphDto> attributes)
    {
        return attributes.Where(a => !a.IsDeleted).Any(a => a.Values.All(v => v.IsDeleted));
    }

    private static void CollapseToMain<TVariant>(List<TVariant> existing)
        where TVariant : EntityVariantGraphDto, new()
    {
        // Nitelik yok → EN AZ BİR ana varyant (ANAVARYANT) GARANTİ: mevcut main/ilk kaydın düzenlemesi korunur;
        // hiç varyant yoksa YENİ ANAVARYANT üretilir (liste asla boş kalmaz — server save'ini beklemeden).
        var main = existing.FirstOrDefault(e => e.IsMain) ?? existing.FirstOrDefault() ?? new TVariant();
        existing.Clear();

        main.IsMain = true;
        main.Code = EntityVariantConsts.MainVariantCode;
        main.Name = EntityVariantConsts.MainVariantName;
        main.AttributeSummary = string.Empty;
        main.CombinationKey = string.Empty;
        existing.Add(main);
    }
}
