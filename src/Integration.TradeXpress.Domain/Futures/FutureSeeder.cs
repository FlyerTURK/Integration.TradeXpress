using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Futures;

/// <summary>
/// Her gerçek tenant'a sistem vadeli enstrümanlarını seed eder (altın saflıkları, metaller, başlıca dövizler) —
/// ERPPROV3 paritesi. FollowingUnit host-paylaşımlı <see cref="CurrencyUnit"/> kataloğundan koda göre çözülür;
/// çarpan saflık/lot faktörüdür. <b>Host'ta (TenantId=null) ÇALIŞMAZ</b> (orchestrator tenant dalında çağrılır).
/// Idempotent: aynı Code varsa atlar. Şirkete-özel enstrümanlar seed'de YOK (kullanıcı CRUD ile ekler).
/// </summary>
public class FutureSeeder(
    IRepository<Future, Guid> futureRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IDataFilter dataFilter,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    // (Kod, Ad, FollowingUnit kodu, Çarpan) — sistem enstrümanları.
    private static readonly (string Code, string Name, string UnitCode, decimal Factor)[] Seeds =
    {
        ("HAS",      "Has Altın",                CurrencyUnitCode.HAS, 1.000m),
        ("HAS(995)", "Has Altın 995",            CurrencyUnitCode.HAS, 0.995m),
        ("HAS(916)", "Has Altın 916 (22 Ayar)",  CurrencyUnitCode.HAS, 0.916m),
        ("HAS(913)", "Has Altın 913",            CurrencyUnitCode.HAS, 0.913m),
        ("USD",      "Amerikan Doları",          CurrencyUnitCode.USD, 1.000m),
        ("EUR",      "Euro",                     CurrencyUnitCode.EUR, 1.000m),
        ("GUM",      "Has Gümüş",                CurrencyUnitCode.GUM, 1.000m),
        ("PLT",      "Has Platin",               CurrencyUnitCode.PLT, 1.000m),
        ("PLD",      "Has Paladyum",             CurrencyUnitCode.PLD, 1.000m),
    };

    /// <summary>Aktif tenant context'inde çalışır → eklenen Future'lar o tenant'a (TenantId) yazılır.</summary>
    public async Task SeedAsync()
    {
        // Host-paylaşımlı CurrencyUnit kataloğu (filter kapalı; birimler TenantId=null) → koda göre Id.
        Dictionary<string, Guid> unitIdByCode;
        using (dataFilter.Disable<IMultiTenant>())
        {
            unitIdByCode = (await currencyUnitRepository.GetQueryableAsync())
                .Where(u => u.TenantId == null)
                .Select(u => new { u.Id, u.Code })
                .ToList()
                .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        }

        // Bu tenant'ta mevcut Future kodları (tenant filtresi açık → yalnız aktif tenant).
        // Soft-delete filtresi KAPALI: silinmiş kayıt da "mevcut" sayılır — silineni diriltme (MetalSeeder deseni).
        List<string> existingCodes;
        using (dataFilter.Disable<ISoftDelete>())
        {
            existingCodes = (await futureRepository.GetQueryableAsync())
                .Select(f => f.Code)
                .ToList();
        }

        var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, name, unitCode, factor) in Seeds)
        {
            if (existing.Contains(code)) continue;
            if (!unitIdByCode.TryGetValue(unitCode, out var uid)) continue;
            await futureRepository.InsertAsync(new Future(code, name, uid, factor), autoSave: false);
        }

        await unitOfWorkManager.Current!.SaveChangesAsync();
    }
}
