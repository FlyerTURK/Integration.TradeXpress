using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Muadil OLMAYAN, <c>StockPolicy.Calculated</c> üründe SATILABİLİR ADET hesabı (ADR-PRODUCT-ORCHESTRATION):
/// varyantın reçetesindeki her metal satırı için <c>eldeki gram / satırın gram ihtiyacı</c> oranının
/// TABAN'ı alınır; satılabilir adet = satırların MİNİMUMU (darboğaz maden belirler).
/// <para>Matematik <c>SubstitutionSolver.PackageCount</c> (min(Available/count)) deseninden devralındı —
/// oradaki muadil-grup kapasitesi neyse, buradaki reçete-genel kapasite odur.</para>
/// <para><b>SAF</b> sınıf: I/O yok, DI yok — mock-first test doğrudan bu sınıfı sahte verilerle çalıştırır.
/// Stok sözlüğünü çağıran doldurur (<see cref="IMetalStockReader"/> gerçek ya da sahte).</para>
/// </summary>
public static class SellableStockCalculator
{
    /// <summary>Reçetenin metal satırlarından satılabilir adet. Kurallar:
    /// <list type="bullet">
    ///   <item>Metal satırı yoksa → null (reçete stoğa bağlı değil; kanal stoğuna dokunulmaz).</item>
    ///   <item>Herhangi bir satırın ihtiyacı ≤ 0 → satır atlanır (bilgi satırı; kapasite kısıtlamaz).</item>
    ///   <item>Stokta OLMAYAN maden = 0 kabul edilir → adet 0 (bilinmeyen iyimser sayılmaz — oversell kapısı).</item>
    ///   <item>Negatif net stok → 0'a kırpılır (kanala asla eksi gitmez).</item>
    ///   <item>Varyantlı satır önce (MetalId, VariantId) anahtarını arar; yoksa (MetalId, null) toplamına düşer.</item>
    /// </list></summary>
    public static int? Calculate(
        IReadOnlyList<RecipeMetalRequirement> requirements,
        IReadOnlyDictionary<(Guid MetalId, Guid? MetalVariantId), decimal> availableByKey)
    {
        // AYNI stok havuzunu paylaşan satırlar TOPLANIR, sonra bölünür (2026-07-25 inceleme bulgusu #22):
        // satır başına bağımsız min almak ortak madeni ÇİFT sayar — 12gr G5 havuzuna 5gr+5gr isteyen iki
        // satır bağımsız değerlendirilince 2 çıkar, doğrusu floor(12/10)=1. Anahtar = satırın ÇÖZÜLMÜŞ stok
        // havuzu (varyant anahtarı varsa o; yoksa metal toplamı) → aynı havuza düşenler birlikte kısıtlar.
        var requiredByKey = new Dictionary<(Guid, Guid?), decimal>();
        var hasConstraint = false;

        foreach (var requirement in requirements)
        {
            if (requirement.RequiredGramsPerUnit <= 0m)
            {
                continue;
            }

            hasConstraint = true;
            var key = ResolvePoolKey(requirement, availableByKey);
            requiredByKey[key] = requiredByKey.GetValueOrDefault(key) + requirement.RequiredGramsPerUnit;
        }

        if (!hasConstraint)
        {
            return null;
        }

        int? sellable = null;
        foreach (var (key, requiredPerUnit) in requiredByKey)
        {
            var available = availableByKey.GetValueOrDefault(key);
            if (available < 0m)
            {
                available = 0m;
            }

            var capacity = (int)Math.Floor(available / requiredPerUnit);
            sellable = sellable is { } current ? Math.Min(current, capacity) : capacity;
        }

        return sellable;
    }

    /// <summary>Satırın stok HAVUZ anahtarı: varyant anahtarı sözlükte varsa o; yoksa metal toplamı (varyantsız
    /// takip dünyası). Toplam geri-düşüşü BİLİNÇLİ: stok varyantsız izleniyorsa varyantlı reçete satırının tek
    /// gerçek kaynağı toplamdır — aynı toplama düşen satırlar yukarıda birlikte toplandığından çift sayılmaz.</summary>
    private static (Guid, Guid?) ResolvePoolKey(
        RecipeMetalRequirement requirement,
        IReadOnlyDictionary<(Guid MetalId, Guid? MetalVariantId), decimal> availableByKey)
    {
        if (requirement.MetalVariantId is { } variantId
            && availableByKey.ContainsKey((requirement.MetalId, variantId)))
        {
            return (requirement.MetalId, variantId);
        }

        return (requirement.MetalId, null);
    }
}

/// <summary>Reçete satırının metal ihtiyacı — bir birim ürün için gereken gram (Amount alanı; adetli emtiada
/// Quantity×StableQuantity'den türetilmiş hâli). Varyantlıysa o varyantın stoğu esas alınır.</summary>
public readonly record struct RecipeMetalRequirement(Guid MetalId, Guid? MetalVariantId, decimal RequiredGramsPerUnit);
