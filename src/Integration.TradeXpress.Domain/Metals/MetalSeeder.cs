using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Her gerçek tenant'a sistem madenlerini seed eder (ERPPROV3 paritesi: gram-altın + sikkeler).
/// FollowingUnit = HAS; işçilik birimleri host CurrencyUnit'ten çözülür. <b>Host'ta ÇALIŞMAZ</b>
/// (orchestrator tenant dalında). Idempotent: kod (normalize) varsa atlar. Tümü adet-takipli (IsQuantity=true).
/// </summary>
public class MetalSeeder(
    IRepository<Metal, Guid> metalRepository,
    IRepository<CurrencyUnit, Guid> currencyUnitRepository,
    IDataFilter dataFilter,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    // (Kod, Ad, Milyem, MilyemOynar, İşçilikTürü, StabilMiktar, GirişİşçiliÄŸi, ÇıkışİşçiliÄŸi, MaliyetBirimKodu)
    private static readonly (string Code, string Name, decimal Purity, bool PurityChange,
        MetalLaborType LaborType, decimal Stable, decimal Entry, decimal Exit, string CostUnit)[] Seeds =
    {
        ("G0.1 GR 995",   "0.10gr 995 Gramaltın",   0.99500m, false, MetalLaborType.Amount,   0.10000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 995",   "0.50gr 995 Gramaltın",   0.99500m, false, MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 995",   "1.00gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   1.00000m, 0.00700m, 0.00700m, CurrencyUnitCode.HAS),
        ("G1.5 GR 995",   "1.50gr 995 Gramaltın",   0.99500m, false, MetalLaborType.Amount,   1.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.0 GR 995",   "2.00gr 995 Gramaltın",   0.99500m, false, MetalLaborType.Amount,   2.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.5 GR 995",   "2.50gr 995 Gramaltın",   1.00000m, true,  MetalLaborType.Amount,   2.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G5.0 GR 995",   "5.00gr 995 Gramaltın",   1.00000m, true,  MetalLaborType.Amount,   5.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G7.0 GR 995",   "7.00gr 995 Gramaltın",   0.99500m, false, MetalLaborType.Amount,   7.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G10 GR 995",    "10.00gr 995 Gramaltın",  1.00000m, true,  MetalLaborType.Amount,  10.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G20 GR 995",    "20.00gr 995 Gramaltın",  1.00000m, true,  MetalLaborType.Amount,  20.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G50 GR 995",    "50.00gr 995 Gramaltın",  0.99800m, true,  MetalLaborType.Amount,  50.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G100 GR 995",   "100.00gr 995 Gramaltın", 0.99800m, true,  MetalLaborType.Amount, 100.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G250 GR 995",   "250.00gr 995 Gramaltın", 0.99500m, false, MetalLaborType.Amount, 250.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G500 GR 995",   "500.00gr 995 Gramaltın", 0.99500m, false, MetalLaborType.Amount, 500.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 916",   "0.50gr 916 Gramaltın",   0.91600m, false, MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 916",   "1.00gr 916 Gramaltın",   0.91600m, false, MetalLaborType.Amount,   1.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.1 GR 9999",  "0.10gr 999.9 Gramaltın", 0.99990m, false, MetalLaborType.Amount,   0.10000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 9999",  "0.50gr 999.9 Gramaltın", 0.99990m, false, MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 9999",  "1.00gr 999.9 Gramaltın", 0.99990m, false, MetalLaborType.Amount,   1.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.5 GR 9999",  "2.50gr 999.9 Gramaltın", 0.99990m, false, MetalLaborType.Amount,   2.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G5.0 GR 9999",  "5.00gr 999.9 Gramaltın", 0.99990m, false, MetalLaborType.Amount,   5.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G10 GR 9999",   "10.00gr 999.9 Gramaltın",0.99990m, false, MetalLaborType.Amount,  10.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G20 GR 9999",   "20.00gr 999.9 Gramaltın",0.99990m, false, MetalLaborType.Amount,  20.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("ONS 9999",      "31.1035gr 999.9 Altın",  0.99990m, false, MetalLaborType.Amount,  31.10350m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G50 GR 9999",   "50.00gr 999.9 Gramaltın",0.99990m, false, MetalLaborType.Amount,  50.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G100 GR 9999",  "100gr 999.9 Gramaltın",  0.99990m, false, MetalLaborType.Amount, 100.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G250 GR 9999",  "250gr 999.9 Gramaltın",  0.99990m, false, MetalLaborType.Amount, 250.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G500 GR 9999",  "500gr 999.9 Gramaltın",  0.99990m, false, MetalLaborType.Amount, 500.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("YCEYREK",       "1.75gr 916 Ziynet Çeyrek", 1.60500m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YYARIM",        "3.50gr 916 Ziynet Yarım",  3.21000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YTEK",          "7.00gr 916 Ziynet Tek",    6.42000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YGRAMISE",      "17.50gr 916 Ziynet Gramise",16.05000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YBESLI",        "35.00gr 916 Ziynet Beşli", 32.05000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YATACEYREK",    "1.80gr 916 Ata Çeyrek",    1.65000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YATAYARIM",     "3.60gr 916 Ata Yarım",     3.30000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YATATEK",       "7.21gr 916 Ata Tek",       6.61000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YATAIKIBUCUK",  "18.02gr 916 Ata İkibuçuk", 16.51000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("YATABESLI",     "36.05gr 916 Ata Beşli",    33.05000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("RCEYREK",       "1.80gr 916 Osmanlı Çeyrek",1.65000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("RYARIM",        "3.60gr 916 Osmanlı Yarım", 3.30000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("RTEK",          "7.20gr 916 Osmanlı Tek",   6.60000m,  true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("RIKIBUCUKLU",   "18.00gr 916 Osmanlı 2.5",  16.50000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
        ("RBESLI",        "36.00gr 916 Osmanlı Beşli",33.00000m, true, MetalLaborType.Quantity, 1.00000m, 0m, 0m, CurrencyUnitCode.USD),
    };

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

        if (!unitIdByCode.TryGetValue(CurrencyUnitCode.HAS, out var hasId))
            return;   // HAS yoksa madenler seed edilemez

        var existing = (await metalRepository.GetQueryableAsync())
            .Select(m => m.Code)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, name, purity, purityChange, laborType, stable, entry, exit, costUnit) in Seeds)
        {
            if (existing.Contains(code.NormalizeAsCode())) continue;
            Guid? costUnitId = unitIdByCode.TryGetValue(costUnit, out var cu) ? cu : null;

            await metalRepository.InsertAsync(
                new Metal(
                    code: code, name: name, followingUnitId: hasId,
                    purity: purity, purityChange: purityChange,
                    isQuantity: true, stableQuantity: stable,
                    laborType: laborType, laborTypeChange: false,
                    entryLabor: entry, entryLaborUnitId: hasId, entryLaborChange: true,
                    exitLabor: exit, exitLaborUnitId: hasId, exitLaborChange: true,
                    costUnitId: costUnitId),
                autoSave: false);
        }

        await unitOfWorkManager.Current!.SaveChangesAsync();
    }
}
