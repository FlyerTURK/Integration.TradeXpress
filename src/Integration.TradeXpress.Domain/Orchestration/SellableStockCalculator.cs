using System;
using System.Collections.Generic;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Muadil OLMAYAN, <c>StockPolicy.Calculated</c> üründe SATILABİLİR ADET hesabı (ADR-PRODUCT-ORCHESTRATION):
/// varyantın reçetesindeki her emtia satırı için <c>eldeki stok / satırın birim ihtiyacı</c> oranının
/// TABAN'ı alınır; satılabilir adet = havuzların MİNİMUMU (darboğaz emtia belirler).
/// <para>Matematik <c>SubstitutionSolver.PackageCount</c> (min(Available/count)) deseninden devralındı —
/// oradaki muadil-grup kapasitesi neyse, buradaki reçete-genel kapasite odur.</para>
/// <para><b>SAF</b> sınıf: I/O yok, DI yok — mock-first test doğrudan bu sınıfı sahte verilerle çalıştırır.
/// Stok sözlüğünü çağıran doldurur (<see cref="ICommodityStockReader"/> gerçek ya da sahte).</para>
/// <para><b>Aile-agnostik</b> (2026-08-06): algoritma DEĞİŞMEDİ, yalnız havuz anahtarı aileyi ve ölçüm
/// boyutunu taşır — çok aileli reçetede darboğaz hangi havuzdan gelirse gelsin aynı min'e girer.</para>
/// </summary>
public static class SellableStockCalculator
{
    /// <summary>Reçetenin emtia satırlarından satılabilir adet. Kurallar:
    /// <list type="bullet">
    ///   <item>Emtia satırı yoksa → null (reçete stoğa bağlı değil; kanal stoğuna dokunulmaz).</item>
    ///   <item>Bir boyuttaki ihtiyaç ≤ 0 → o boyut atlanır (satır o boyutta kısıt getirmiyor demektir).</item>
    ///   <item>Stokta OLMAYAN emtia = 0 kabul edilir → adet 0 (bilinmeyen iyimser sayılmaz — oversell kapısı).</item>
    ///   <item>Negatif net stok → 0'a kırpılır (kanala asla eksi gitmez).</item>
    ///   <item>Varyantlı satır önce (aile, emtia, varyant) anahtarını arar; yoksa (aile, emtia, null) toplamına düşer.</item>
    /// </list></summary>
    public static int? Calculate(
        IReadOnlyList<RecipeCommodityRequirement> requirements,
        IReadOnlyDictionary<CommodityStockKey, CommodityAvailability> availableByKey)
    {
        // AYNI stok havuzunu paylaşan satırlar TOPLANIR, sonra bölünür (2026-07-25 inceleme bulgusu #22):
        // satır başına bağımsız min almak ortak emtiayı ÇİFT sayar — 12gr G5 havuzuna 5gr+5gr isteyen iki
        // satır bağımsız değerlendirilince 2 çıkar, doğrusu floor(12/10)=1. Anahtar = satırın ÇÖZÜLMÜŞ stok
        // havuzu (varyant anahtarı varsa o; yoksa emtia toplamı) + ÖLÇÜM BOYUTU — gram havuzuyla adet havuzu
        // aynı emtiada bile ayrı kısıttır, toplanamazlar.
        var requiredByPool = new Dictionary<(CommodityStockKey Key, CommodityStockDimension Dimension), decimal>();

        foreach (var requirement in requirements)
        {
            var key = ResolvePoolKey(requirement, availableByKey);
            Accumulate(requiredByPool, key, CommodityStockDimension.Amount, requirement.RequiredAmountPerUnit);
            Accumulate(requiredByPool, key, CommodityStockDimension.Quantity, requirement.RequiredQuantityPerUnit);
        }

        if (requiredByPool.Count == 0)
        {
            return null;
        }

        int? sellable = null;
        foreach (var (pool, requiredPerUnit) in requiredByPool)
        {
            var available = availableByKey.GetValueOrDefault(pool.Key).In(pool.Dimension);
            if (available < 0m)
            {
                available = 0m;
            }

            var capacity = (int)Math.Floor(available / requiredPerUnit);
            sellable = sellable is { } current ? Math.Min(current, capacity) : capacity;
        }

        return sellable;
    }

    /// <summary>İhtiyacı havuza ekler. ≤ 0 ihtiyaç kısıt DEĞİLDİR (bilgi satırı) — havuzu hiç açmaz,
    /// aksi halde sıfıra bölme olurdu.</summary>
    private static void Accumulate(
        Dictionary<(CommodityStockKey, CommodityStockDimension), decimal> pools,
        CommodityStockKey key,
        CommodityStockDimension dimension,
        decimal requiredPerUnit)
    {
        if (requiredPerUnit <= 0m)
        {
            return;
        }

        var pool = (key, dimension);
        pools[pool] = pools.GetValueOrDefault(pool) + requiredPerUnit;
    }

    /// <summary>Satırın stok HAVUZ anahtarı: varyant anahtarı sözlükte varsa o; yoksa emtia toplamı (varyantsız
    /// takip dünyası). Toplam geri-düşüşü BİLİNÇLİ: stok varyantsız izleniyorsa varyantlı reçete satırının tek
    /// gerçek kaynağı toplamdır — aynı toplama düşen satırlar yukarıda birlikte toplandığından çift sayılmaz.</summary>
    private static CommodityStockKey ResolvePoolKey(
        RecipeCommodityRequirement requirement,
        IReadOnlyDictionary<CommodityStockKey, CommodityAvailability> availableByKey)
    {
        if (requirement.CommodityVariantId is { } variantId)
        {
            var variantKey = new CommodityStockKey(requirement.Family, requirement.CommodityId, variantId);
            if (availableByKey.ContainsKey(variantKey))
            {
                return variantKey;
            }
        }

        return new CommodityStockKey(requirement.Family, requirement.CommodityId, null);
    }
}

/// <summary>
/// Reçete satırının bir birim ürün için emtia ihtiyacı — <b>iki boyutta birden</b>.
/// <list type="bullet">
///   <item><see cref="RequiredAmountPerUnit"/> — miktar (Metal/Scrap/Future'da GRAM: adetli emtiada
///   <c>Quantity × StableQuantity</c>'den türetilmiş hâli; Good'da stok-birimi miktarı).</item>
///   <item><see cref="RequiredQuantityPerUnit"/> — ADET.</item>
/// </list>
/// <para>İkisi de dolu olabilir: satır "3 adet <b>ve</b> 1,5 kg" diyorsa ikisi de gerçek kısıttır ve ikisi de
/// hesaba girer. 0 olan boyut kısıt saymaz. Bu, ihtiyacın tek bir sayıya indirgenip biriminin varsayımla
/// seçilmesinden kaçınır — o varsayım Metal'de bir kez oversell'e yol açmıştı.</para>
/// <para>Varyantlıysa o varyantın stoğu esas alınır (yoksa emtia toplamına düşülür).</para>
/// </summary>
public readonly record struct RecipeCommodityRequirement(
    ProcessType Family,
    Guid CommodityId,
    Guid? CommodityVariantId,
    decimal RequiredAmountPerUnit,
    decimal RequiredQuantityPerUnit);
