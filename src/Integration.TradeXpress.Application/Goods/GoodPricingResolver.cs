using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Goods;

/// <summary>Bir mamülün "temsili fiyatı" — ANA VARYANTININ (IsMain) <see cref="GoodVariantDetail"/> fiyatı.
/// Fiyat artık varyant seviyesinde (VP4); voucher/on-hand ise Good.Id'ye referanslı → tüketiciler (bilanço +
/// voucher-liste) bir Good için TEK fiyata ihtiyaç duyar = ana varyantınki. TEK KAYNAK (DRY) — iki tüketici de bunu çağırır.</summary>
public interface IGoodPricingResolver
{
    /// <summary>Verilen Good'ların ana-varyant fiyat özetini döner (ana varyantı/detayı olmayan Good sözlükte YOK).</summary>
    Task<Dictionary<Guid, GoodPricingSnapshot>> ResolveAsync(IReadOnlyCollection<Guid> goodIds);

    /// <summary>BELİRLİ VARYANTLARIN fiyat özetini döner (anahtar = <c>EntityVariant.Id</c>) — reçete satırı
    /// ana-dışı bir varyant seçtiğinde kullanılır.
    ///
    /// <para><b>Neden gerekli:</b> <see cref="ResolveAsync"/> bir Good için TEK (ana varyant) fiyat verir; bu,
    /// voucher/bilanço gibi Good.Id'ye referanslı tüketiciler için doğrudur ama REÇETE satırı varyant seçebilir.
    /// Ana-varyant fiyatına düşmek, 14/18/22 ayar üç varyantı aynı maliyetle hesaplamak demekti —
    /// farklı maliyetli varyantlar tek fiyata çökerdi. Metal'de aynı sorun varyant-anahtarlı çözümle kapatılmıştı;
    /// bu, o desenin Good karşılığıdır.</para>
    ///
    /// <para>Detayı olmayan varyant sözlükte YOKTUR (çağıran ana-varyant fallback'ine düşebilir).</para></summary>
    Task<Dictionary<Guid, GoodPricingSnapshot>> ResolveByVariantAsync(IReadOnlyCollection<Guid> variantIds);
}

/// <summary>Bir mamülün temsili (ana varyant) fiyat özeti — alış/satış + birimler.</summary>
public readonly record struct GoodPricingSnapshot(
    decimal EntryPrice, Guid? EntryPriceUnitId, decimal ExitPrice, Guid? ExitPriceUnitId);

public class GoodPricingResolver : IGoodPricingResolver, ITransientDependency
{
    // Agnostik varyant sisteminde Good varyantlarının EntityName anahtarı (GoodAppService.GoodEntityName ile aynı).
    private const string GoodEntityName = "Good";

    private readonly IRepository<EntityVariant, Guid> _variants;
    private readonly IRepository<GoodVariantDetail, Guid> _details;
    private readonly IAsyncQueryableExecuter _executer;

    public GoodPricingResolver(
        IRepository<EntityVariant, Guid> variants,
        IRepository<GoodVariantDetail, Guid> details,
        IAsyncQueryableExecuter executer)
    {
        _variants = variants;
        _details = details;
        _executer = executer;
    }

    public async Task<Dictionary<Guid, GoodPricingSnapshot>> ResolveAsync(IReadOnlyCollection<Guid> goodIds)
    {
        var result = new Dictionary<Guid, GoodPricingSnapshot>();
        if (goodIds.Count == 0)
        {
            return result;
        }

        // Her Good'un ANA varyantı (IsMain) → (Good.Id, VariantId).
        var mainVariants = await _executer.ToListAsync(
            (await _variants.GetQueryableAsync())
                .Where(v => v.EntityName == GoodEntityName && goodIds.Contains(v.EntityId) && v.IsMain)
                .Select(v => new { v.EntityId, v.Id }));
        if (mainVariants.Count == 0)
        {
            return result;
        }

        var goodByVariant = mainVariants.ToDictionary(x => x.Id, x => x.EntityId);
        var variantIds = mainVariants.Select(x => x.Id).ToList();

        var details = await _executer.ToListAsync(
            (await _details.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId)));

        foreach (var d in details)
        {
            if (goodByVariant.TryGetValue(d.EntityVariantId, out var goodId))
            {
                result[goodId] = new GoodPricingSnapshot(d.EntryPrice, d.EntryPriceUnitId, d.ExitPrice, d.ExitPriceUnitId);
            }
        }

        return result;
    }

    public async Task<Dictionary<Guid, GoodPricingSnapshot>> ResolveByVariantAsync(IReadOnlyCollection<Guid> variantIds)
    {
        var result = new Dictionary<Guid, GoodPricingSnapshot>();
        if (variantIds.Count == 0)
        {
            return result;
        }

        // Ana-varyant filtresi YOK: anahtar doğrudan varyant kimliğidir. EntityName kontrolü de gerekmez —
        // varyant kimliği zaten tekildir ve GoodVariantDetail yalnız Good varyantlarına bağlanır.
        var details = await _executer.ToListAsync(
            (await _details.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId)));

        foreach (var d in details)
        {
            result[d.EntityVariantId] = new GoodPricingSnapshot(d.EntryPrice, d.EntryPriceUnitId, d.ExitPrice, d.ExitPriceUnitId);
        }

        return result;
    }
}
