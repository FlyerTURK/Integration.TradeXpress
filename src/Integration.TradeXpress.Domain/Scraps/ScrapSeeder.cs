using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Scraps;

/// <summary>
/// Her gerçek tenant'a sistem hurda madenlerini seed eder (ERPPROV3 paritesi; ayar bazlı milyemler).
/// FollowingUnit host-paylaşımlı <see cref="CurrencyUnit"/> kataloğundan koda göre çözülür.
/// <b>Host'ta (TenantId=null) ÇALIŞMAZ</b> (orchestrator tenant dalında çağrılır). FactorChange=true (milyem oynar).
/// Idempotent: kod (normalize edilmiş haliyle) varsa atlar.
/// </summary>
public class ScrapSeeder(
    IRepository<Scrap, Guid> scrapRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IDataFilter dataFilter,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    // (Kod, Ad, FollowingUnit kodu, Factor). MilyemOynarmi=1 → FactorChange=true.
    private static readonly (string Code, string Name, string UnitCode, decimal Factor)[] Seeds =
    {
        ("08 HURDA", "08 Ayar Hurda", CurrencyUnitCode.HAS, 0.33300m),
        ("09 HURDA", "09 Ayar Hurda", CurrencyUnitCode.HAS, 0.37500m),
        ("10 HURDA", "10 Ayar Hurda", CurrencyUnitCode.HAS, 0.41600m),
        ("14 HURDA", "14 Ayar Hurda", CurrencyUnitCode.HAS, 0.58500m),
        ("18 HURDA", "18 Ayar Hurda", CurrencyUnitCode.HAS, 0.75000m),
        ("21 HURDA", "21 Ayar Hurda", CurrencyUnitCode.HAS, 0.87500m),
        ("22 HURDA", "22 Ayar Hurda", CurrencyUnitCode.HAS, 0.91600m),
        ("24 HURDA", "24 Ayar Hurda", CurrencyUnitCode.HAS, 0.99500m),
        ("995",      "Has Altın",     CurrencyUnitCode.HAS, 0.99500m),
        ("9999",     "Has Altın",     CurrencyUnitCode.HAS, 0.99990m),
    };

    /// <summary>Aktif tenant context'inde çalışır → eklenen Scrap'lar o tenant'a (TenantId) yazılır.</summary>
    public async Task SeedAsync()
    {
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

        // NOT: legacy '_' rename-backfill KALDIRILDI (2026-07-05): tüm DB'lerdeki '_'li seed kodları
        // boşukluya taşındı/temizlendi; yeni normalize '_' üretmez → backfill kalıcı no-op olmuştu.
        var existing = (await scrapRepository.GetQueryableAsync())
            .Select(s => s.Code)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, name, unitCode, factor) in Seeds)
        {
            // Entity ctor kodu normalize ettiği için var-kontrolü normalize edilmiş kodla yapılır.
            if (existing.Contains(code.NormalizeAsCode())) continue;
            if (!unitIdByCode.TryGetValue(unitCode, out var uid)) continue;
            await scrapRepository.InsertAsync(new Scrap(code, name, uid, factor, factorChange: true), autoSave: false);
        }

        await unitOfWorkManager.Current!.SaveChangesAsync();
    }
}
