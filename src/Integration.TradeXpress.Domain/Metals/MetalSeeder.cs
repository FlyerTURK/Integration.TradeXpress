using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    IRepository<EntityVariant, Guid> entityVariantRepository,
    IRepository<MetalVariantDetail, Guid> metalVariantDetailRepository,
    IRepository<Company, Guid> companyRepository,
    IDataFilter dataFilter,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    private const string MetalEntityName = "Metal";

    // GRAM ALTIN KOD/MİLYEM KURALI (2026-07-25 Hakan düzeltmesi — seeder'da sapma birikmişti):
    //  1) Kod gramajı TEK ONDALIKLI yazılır: G10 DEĞİL "G10.0", G100 DEĞİL "G100.0". Eskiden tam sayı
    //     gramajlar ondalıksız yazılmıştı (G10/G20/G50/G100/G250/G500) → aynı ailede iki farklı yazım.
    //  2) Kodun SON EKİ milyemi belirler ve OTORİTERDİR: "995" → 0.99500 · "9999" → 0.99990 · "916" → 0.91600.
    //     Eskiden bazı 995'ler 1.00000 (saf altın!) ya da 0.99800 milyemle kayıtlıydı — kod ile veri çelişiyordu.
    //  3) 995 gram altın İŞÇİLİKLERİ (giriş = çıkış, birim HAS): 1gr ve 2.5gr → 0.007 · 5/10/20gr → 0.005 ·
    //     50/100gr → 0.003. Listede olmayan gramajlar (0.1/0.5/1.5/2.0/7.0/250/500) işçiliksizdir.
    // (Kod, Ad, Milyem, MilyemOynar, İşçilikTürü, StabilMiktar, Girişİşçiliği, Çıkışİşçiliği, MaliyetBirimKodu)
    private static readonly (string Code, string Name, decimal Factor, bool FactorChange,
        MetalLaborType LaborType, decimal Stable, decimal Entry, decimal Exit, string CostUnit)[] Seeds =
    {
        ("G0.1 GR 995",   "0.10gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   0.10000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 995",   "0.50gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 995",   "1.00gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   1.00000m, 0.00700m, 0.00700m, CurrencyUnitCode.HAS),
        ("G1.5 GR 995",   "1.50gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   1.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.0 GR 995",   "2.00gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   2.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.5 GR 995",   "2.50gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   2.50000m, 0.00700m, 0.00700m, CurrencyUnitCode.HAS),
        ("G5.0 GR 995",   "5.00gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   5.00000m, 0.00500m, 0.00500m, CurrencyUnitCode.HAS),
        ("G7.0 GR 995",   "7.00gr 995 Gramaltın",   0.99500m, true,  MetalLaborType.Amount,   7.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G10.0 GR 995",  "10.00gr 995 Gramaltın",  0.99500m, true,  MetalLaborType.Amount,  10.00000m, 0.00500m, 0.00500m, CurrencyUnitCode.HAS),
        ("G20.0 GR 995",  "20.00gr 995 Gramaltın",  0.99500m, true,  MetalLaborType.Amount,  20.00000m, 0.00500m, 0.00500m, CurrencyUnitCode.HAS),
        ("G50.0 GR 995",  "50.00gr 995 Gramaltın",  0.99500m, true,  MetalLaborType.Amount,  50.00000m, 0.00300m, 0.00300m, CurrencyUnitCode.HAS),
        ("G100.0 GR 995", "100.00gr 995 Gramaltın", 0.99500m, true,  MetalLaborType.Amount, 100.00000m, 0.00300m, 0.00300m, CurrencyUnitCode.HAS),
        ("G250.0 GR 995", "250.00gr 995 Gramaltın", 0.99500m, true,  MetalLaborType.Amount, 250.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G500.0 GR 995", "500.00gr 995 Gramaltın", 0.99500m, true,  MetalLaborType.Amount, 500.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 916",   "0.50gr 916 Gramaltın",   0.91600m, true,  MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 916",   "1.00gr 916 Gramaltın",   0.91600m, true,  MetalLaborType.Amount,   1.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.1 GR 9999",  "0.10gr 999.9 Gramaltın", 0.99990m, true,  MetalLaborType.Amount,   0.10000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G0.5 GR 9999",  "0.50gr 999.9 Gramaltın", 0.99990m, true,  MetalLaborType.Amount,   0.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G1.0 GR 9999",  "1.00gr 999.9 Gramaltın", 0.99990m, true,  MetalLaborType.Amount,   1.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G2.5 GR 9999",  "2.50gr 999.9 Gramaltın", 0.99990m, true,  MetalLaborType.Amount,   2.50000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G5.0 GR 9999",  "5.00gr 999.9 Gramaltın", 0.99990m, true,  MetalLaborType.Amount,   5.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G10.0 GR 9999", "10.00gr 999.9 Gramaltın",0.99990m, true,  MetalLaborType.Amount,  10.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G20.0 GR 9999", "20.00gr 999.9 Gramaltın",0.99990m, true,  MetalLaborType.Amount,  20.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("ONS 9999",      "31.1035gr 999.9 Altın",  0.99990m, true,  MetalLaborType.Amount,  31.10350m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G50.0 GR 9999", "50.00gr 999.9 Gramaltın",0.99990m, true,  MetalLaborType.Amount,  50.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G100.0 GR 9999","100gr 999.9 Gramaltın",  0.99990m, true,  MetalLaborType.Amount, 100.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G250.0 GR 9999","250gr 999.9 Gramaltın",  0.99990m, true,  MetalLaborType.Amount, 250.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
        ("G500.0 GR 9999","500gr 999.9 Gramaltın",  0.99990m, true,  MetalLaborType.Amount, 500.00000m, 0m,       0m,       CurrencyUnitCode.HAS),
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

        var companies = await companyRepository.GetListAsync();

        foreach (var company in companies)
        {
            // SOFT-DELETE edilmiş kayıtlar da "mevcut" sayılır (kod-inceleme bulgusu): soft-delete filtresi
            // KAPALI okunur. Aksi halde kullanıcının bilinçli olarak sildiği bir maden, sonraki HER DbMigrator
            // koşusunda yeniden INSERT ediliyordu — "çifter kayıt" temizlikleri bu yüzden kalıcı olmuyordu
            // (81 satır silinmiş, sonra geri gelmişti). Seeder eksik olanı tamamlar, sileneni DİRİLTMEZ.
            List<string> existingCodes;
            using (dataFilter.Disable<ISoftDelete>())
            {
                existingCodes = (await metalRepository.GetQueryableAsync())
                    .Where(m => m.CompanyId == company.Id)
                    .Select(m => m.Code)
                    .ToList();
            }

            var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // ANA VARYANT KİMLİK ONARIMI (2026-08-08) — seeder yalnız EKSİĞİ tamamlıyordu, MEVCUDU hiç
            // denetlemiyordu. Sonuç: 2026-08-06'daki "varyant kodu sahibi izler" kararından ÖNCE ekilmiş 86
            // madenin ana varyantı "ANAVARYANT" sentinel koduyla kaldı ve onarım hiç uğramadı (kayıt zaten
            // var → atla). O kod pazaryerine SKU olarak gidebilirdi. Artık her koşuda mevcut madenlerin ana
            // varyant kimliği de sahibine eşitlenir — seeder "eksiği tamamla" değil "doğru hâle getir" olur.
            // Sileneni DİRİLTMEZ kuralı korunur: yalnız CANLI madenlerin varyantına dokunulur.
            await RepairMainVariantIdentityAsync(company.Id);

            foreach (var (code, name, factor, factorChange, laborType, stable, entry, exit, costUnit) in Seeds)
            {
            if (existing.Contains(code.NormalizeAsCode())) continue;
            Guid? costUnitId = unitIdByCode.TryGetValue(costUnit, out var cu) ? cu : null;

                var metal = new Metal(
                    code: code, name: name, followingUnitId: hasId,
                    companyId: company.Id,
                    factor: factor, factorChange: factorChange,
                    isQuantity: true, stableQuantity: stable);
                await metalRepository.InsertAsync(metal, autoSave: false);

                // Ana varyant kimliği SAHİPTEN (2026-08-06 Hakan kararı: "ANAVARYANT boşa çıkmalı") —
                // EntityVariantManager.EnsureMainVariantAsync'in owner-kimlikli yoluyla AYNI kural. Sentinel
                // yalnız sahip kimliği bilinmeyen savunma yolunda kaldı; seeder sahibi biliyor.
                var variant = new EntityVariant(
                    companyId: company.Id,
                    entityName: "Metal",
                    entityId: metal.Id,
                    code: metal.Code,
                    name: metal.Name,
                    isMain: true,
                    isActive: true);
                await entityVariantRepository.InsertAsync(variant, autoSave: false);

                var detail = new MetalVariantDetail(companyId: company.Id, entityVariantId: variant.Id);
                detail.SetLabor(
                    laborType: laborType, laborTypeChange: false,
                    entryLabor: entry, entryLaborUnitId: hasId, entryLaborChange: true,
                    exitLabor: exit, exitLaborUnitId: hasId, exitLaborChange: true,
                    costUnitId: costUnitId);
                await metalVariantDetailRepository.InsertAsync(detail, autoSave: false);
            }
        }

        await unitOfWorkManager.Current!.SaveChangesAsync();
    }

    /// <summary>Şirketin CANLI madenlerinde ana varyant kimliğini sahibine eşitler (kod + ad) ve pasif kalmış
    /// ana varyantı aktifleştirir.
    ///
    /// <para><b>Neden seeder'da:</b> "varyant kodu sahibi izler" kuralı yalnız KAYIT anında çalışan bir
    /// onarımdı; hiç kaydedilmemiş kayda uğramıyordu. Seeder ise "kod varsa atla" dediği için mevcut kayda
    /// hiç bakmıyordu. İkisinin arasında kalan 86 maden sentinel koduyla ("ANAVARYANT") kaldı — o kod
    /// pazaryerine SKU olarak gidebilecek durumdaydı. Artık her koşuda mevcut kayıtlar da doğrulanır.</para>
    ///
    /// <para><b>Idempotent ve muhafazakâr:</b> yalnız FARK varsa yazar (gereksiz UPDATE yok), yalnız ana
    /// varyanta dokunur (kombinasyon varyantlarının kodu değer adlarından türer), soft-delete edilmiş madenin
    /// varyantına DOKUNMAZ — "sileneni diriltme" kuralının varyant ayağı.</para></summary>
    private async Task RepairMainVariantIdentityAsync(Guid companyId)
    {
        var metals = (await metalRepository.GetQueryableAsync())
            .Where(m => m.CompanyId == companyId)
            .Select(m => new { m.Id, m.Code, m.Name })
            .ToList();

        if (metals.Count == 0)
        {
            return;
        }

        var metalIds = metals.Select(m => m.Id).ToList();
        var mains = (await entityVariantRepository.GetQueryableAsync())
            .Where(v => v.EntityName == MetalEntityName && v.IsMain && metalIds.Contains(v.EntityId))
            .ToList();

        var ownerById = metals.ToDictionary(m => m.Id, m => (m.Code, m.Name));

        foreach (var main in mains)
        {
            if (!ownerById.TryGetValue(main.EntityId, out var owner))
            {
                continue;
            }

            var changed = false;

            if (!string.Equals(main.Code, owner.Code, StringComparison.Ordinal))
            {
                main.SetCode(owner.Code);
                changed = true;
            }

            if (!string.Equals(main.Name, owner.Name, StringComparison.Ordinal))
            {
                main.SetName(owner.Name);
                changed = true;
            }

            // Ana varyant pasif olamaz (2026-08-08 kuralı) — eski veride kalmış ihlali burada onar.
            if (!main.IsActive)
            {
                main.SetAsMain(true);   // SetAsMain aktifleştirir; SetActive(true) de olurdu, niyet burada açık
                changed = true;
            }

            if (changed)
            {
                await entityVariantRepository.UpdateAsync(main, autoSave: false);
            }
        }
    }
}
