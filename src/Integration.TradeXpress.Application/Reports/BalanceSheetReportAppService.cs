using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Bullions;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Reports.BalanceSheet;
using Integration.TradeXpress.Vouchers.Balance;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Bilanço raporu (FULL net-varlık; pozisyon = exposure alt-kümesi). Tüm pluggable kategori kaynaklarını
/// (<see cref="IBalanceSheetCategorySource"/>) kapsam+tarihle toplar, base (bilanço) birimine re-base'li değerler
/// (pozisyonla AYNI <c>IEffectivePriceAppService</c>), kategori toplamı + TOPLAM (net varlık) üretir.
/// Sızıntı önleme: CompanyId DAİMA working-context'ten (<c>ICurrentCompany</c>) zorlanır.
/// </summary>
[Authorize(TradeXpressPermissions.Reports.BalanceSheet)]
public class BalanceSheetReportAppService : TradeXpressAppService, IBalanceSheetReportAppService
{
    private readonly IEnumerable<IBalanceSheetCategorySource> _sources;
    private readonly IRepository<Branch, Guid> _branches;
    private readonly IRepository<Company, Guid> _companies;
    private readonly IRepository<CurrencyUnit, Guid> _units;
    private readonly IRepository<BalanceLedgerEntry, Guid> _ledger;
    private readonly IRepository<Account, Guid> _accounts;
    private readonly IRepository<SubAccount, Guid> _subAccounts;
    private readonly IMetalReportAppService _metalReport;
    private readonly IScrapReportAppService _scrapReport;
    private readonly ICashReportAppService _cashReport;
    private readonly IEffectivePriceAppService _pricing;
    private readonly ICurrentCompany _currentCompany;
    private readonly IRepository<BalanceSheetSnapshot, Guid> _snapshots;

    public BalanceSheetReportAppService(
        IEnumerable<IBalanceSheetCategorySource> sources,
        IRepository<Branch, Guid> branches,
        IRepository<Company, Guid> companies,
        IRepository<CurrencyUnit, Guid> units,
        IRepository<BalanceLedgerEntry, Guid> ledger,
        IRepository<Account, Guid> accounts,
        IRepository<SubAccount, Guid> subAccounts,
        IMetalReportAppService metalReport,
        IScrapReportAppService scrapReport,
        ICashReportAppService cashReport,
        IEffectivePriceAppService pricing,
        ICurrentCompany currentCompany,
        IRepository<BalanceSheetSnapshot, Guid> snapshots)
    {
        _sources        = sources;
        _branches       = branches;
        _companies      = companies;
        _units          = units;
        _ledger         = ledger;
        _accounts       = accounts;
        _subAccounts    = subAccounts;
        _metalReport    = metalReport;
        _scrapReport    = scrapReport;
        _cashReport     = cashReport;
        _pricing        = pricing;
        _currentCompany = currentCompany;
        _snapshots      = snapshots;
    }

    /// <summary>
    /// DRILL — bir kategori×birim değerinin oluştuğu HAREKETLER (çift-tık popup; Kod=belge no, Bakiye=firma-perspektifi).
    /// BAKİYE(cari) = ledger entries (VoucherNumber + −Amount; BAKİYE = −Σ(ledger) olduğundan işaret görünenle aynı).
    /// Diğer kategoriler (İşçilik/Stok/Taş/Mücevher) HENÜZ desteklenmiyor → Supported=false. Kapsam ComputeAsync ile AYNI.
    /// </summary>
    public virtual async Task<BalanceSheetMovementResultDto> GetMovementsAsync(BalanceSheetMovementRequestDto input)
    {
        var result = new BalanceSheetMovementResultDto();
        if (_currentCompany.Id is not { } companyId)
            return result;

        result.UnitCode = input.UnitId == BullionConsts.PseudoUnitId
            ? CurrencyUnitCode.Bullion   // TAKOZ pseudo-birim (CurrencyUnit'te yok)
            : (await AsyncExecuter.FirstOrDefaultAsync(
                (await _units.GetQueryableAsync()).Where(u => u.Id == input.UnitId).Select(u => u.Code)) ?? string.Empty);

        var cutoff   = input.AsOf.Date.AddDays(1);
        // DİKKAT: drill kapsamı şube doğrulaması YAPMAZ (mevcut davranış) — ResolveScopedBranchIdAsync'ten kasıtlı farklı.
        var branchId = input.Scope == BalanceSheetScope.Company ? (Guid?)null : input.BranchId;

        // Kategori → hareket sağlayıcısı (her kategori kendi private metodunda; davranış birebir).
        if (input.Category == BalanceSheetCategory.AccountBalance)
        {
            result.Supported = true;
            result.Movements = await GetAccountBalanceMovementsAsync(companyId, branchId, input.UnitId, cutoff);
        }
        else if (input.Category == BalanceSheetCategory.Labor)
        {
            result.Supported = true;
            result.Movements = await GetLaborMovementsAsync(branchId, input.UnitId, cutoff);
        }
        else if (input.Category == BalanceSheetCategory.Stock)
        {
            result.Supported = true;
            result.Movements = await GetStockMovementsAsync(branchId, input.UnitId, cutoff);
        }
        else if (input.Category == BalanceSheetCategory.Stone || input.Category == BalanceSheetCategory.Jewelry
              || input.Category == BalanceSheetCategory.Good)
        {
            // Kaynak drill desteklemiyorsa Supported=false kalır (mevcut davranış).
            var movements = await GetCommodityDrillMovementsAsync(input.Category, companyId, branchId, input.AsOf, input.UnitId);
            if (movements != null)
            {
                result.Supported = true;
                result.Movements = movements;
            }
        }

        return result;
    }

    /// <summary>
    /// BAKİYE(cari) drill: CARİ/ALTHESAP bazında kır (belge no DEĞİL — kullanıcı cari kodu ister):
    /// (Account, SubAccount) GROUP BY + SUM; BAKİYE = −Σ(ledger) olduğundan işaret görünenle aynı.
    /// </summary>
    private async Task<List<BalanceSheetMovementDto>> GetAccountBalanceMovementsAsync(
        Guid companyId, Guid? branchId, Guid unitId, DateTime cutoff)
    {
        var q = await _ledger.GetQueryableAsync();
        var grouped = await AsyncExecuter.ToListAsync(
            from e in q
            where e.CompanyId == companyId
               && (branchId == null || e.BranchId == branchId)
               && e.UnitId == unitId
               && e.VoucherDate < cutoff
            group e by new { e.AccountId, e.SubAccountId } into g
            select new { g.Key.AccountId, g.Key.SubAccountId, Amount = g.Sum(x => x.Amount) });

        // Kodları memory'de çöz (Account zorunlu; SubAccount opsiyonel → "cari / althesap").
        var accountIds = grouped.Select(r => r.AccountId).Distinct().ToList();
        var subIds     = grouped.Where(r => r.SubAccountId != null).Select(r => r.SubAccountId!.Value).Distinct().ToList();

        var accountCodes = (await AsyncExecuter.ToListAsync(
                (await _accounts.GetQueryableAsync()).Where(a => accountIds.Contains(a.Id)).Select(a => new { a.Id, a.Code })))
            .ToDictionary(a => a.Id, a => a.Code);
        var subCodes = subIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await AsyncExecuter.ToListAsync(
                (await _subAccounts.GetQueryableAsync()).Where(s => subIds.Contains(s.Id)).Select(s => new { s.Id, s.Code })))
              .ToDictionary(s => s.Id, s => s.Code);

        return grouped
            .Select(r => new BalanceSheetMovementDto
            {
                Code = r.SubAccountId is { } sid && subCodes.TryGetValue(sid, out var sc)
                    ? $"{accountCodes.GetValueOrDefault(r.AccountId, "?")} / {sc}"
                    : accountCodes.GetValueOrDefault(r.AccountId, "?"),
                Amount = -r.Amount,
            })
            .Where(m => m.Amount != 0m)
            .OrderBy(m => m.Code)
            .ToList();
    }

    /// <summary>İŞÇİLİK drill = on-hand metal işçilik maliyeti, COMMODITY (metal kodu) bazında. +Net (varlık, NEGATİFLENMEZ).</summary>
    private async Task<List<BalanceSheetMovementDto>> GetLaborMovementsAsync(Guid? branchId, Guid unitId, DateTime cutoff)
    {
        var byCode = await _metalReport.GetMetalLaborByCommodityAsync(branchId, unitId, cutoff);
        return ToSortedMovements(byCode);
    }

    /// <summary>STOK drill = fiziksel maden + hurda (COMMODITY bazında) + nakit (tek "NAKİT" lump). +Net (firma-perspektifi).</summary>
    private async Task<List<BalanceSheetMovementDto>> GetStockMovementsAsync(Guid? branchId, Guid unitId, DateTime cutoff)
    {
        var merged = new Dictionary<string, decimal>();
        foreach (var kv in await _metalReport.GetMetalStockByCommodityAsync(branchId, unitId, cutoff))
        {
            merged[kv.Key] = merged.GetValueOrDefault(kv.Key) + kv.Value;
        }
        foreach (var kv in await _scrapReport.GetScrapStockByCommodityAsync(branchId, unitId, cutoff))
        {
            merged[kv.Key] = merged.GetValueOrDefault(kv.Key) + kv.Value;
        }
        var cash = (await _cashReport.GetCashNetByUnitAsync(branchId, vaultId: null, asOfExclusive: cutoff))
            .GetValueOrDefault(unitId);
        if (cash != 0m)
        {
            merged["NAKİT"] = merged.GetValueOrDefault("NAKİT") + cash;
        }

        return ToSortedMovements(merged);
    }

    /// <summary>TAŞ/MÜCEVHER drill = maliyet-envanteri; ilgili kaynağın COMMODITY (taş/mücevher kodu) kırılımı. +Net.
    /// Kategoriye drill kaynağı yoksa <c>null</c> (çağıran Supported=false bırakır).</summary>
    private async Task<List<BalanceSheetMovementDto>?> GetCommodityDrillMovementsAsync(
        string category, Guid companyId, Guid? branchId, DateTime asOf, Guid unitId)
    {
        var drill = _sources.OfType<IBalanceSheetCommodityDrill>()
            .FirstOrDefault(d => d.DrillCategory == category);
        if (drill == null)
        {
            return null;
        }

        var byCode = await drill.GetCommodityBreakdownAsync(companyId, branchId, asOf, unitId);
        return ToSortedMovements(byCode);
    }

    /// <summary>Kod→tutar kırılımını sıfırları atıp koda göre sıralı hareket satırlarına çevirir (drill'lerin ortak kuyruğu).</summary>
    private static List<BalanceSheetMovementDto> ToSortedMovements(IEnumerable<KeyValuePair<string, decimal>> byCode)
    {
        return byCode
            .Where(kv => kv.Value != 0m)
            .Select(kv => new BalanceSheetMovementDto { Code = kv.Key, Amount = kv.Value })
            .OrderBy(m => m.Code)
            .ToList();
    }

    public virtual async Task<BalanceSheetReportResultDto> ComputeAsync(BalanceSheetReportFilterDto filter)
    {
        // Working şirket yoksa (host/API) boş — client CompanyId GÜVENİLMEZ, ambient'ten zorlanır.
        if (_currentCompany.Id is not { } companyId)
            return new();

        // Company scope → branchId null (konsolide). Branch scope → client şubesi (şirkete ait değilse düşür).
        var branchId = await ResolveScopedBranchIdAsync(filter.Scope, filter.BranchId, companyId);

        var baseUnitId = await ResolveBaseUnitAsync(companyId, branchId);

        // ① Tüm kaynakların kategori-bazlı katkılarını topla (sıraya göre; bir kaynak birden çok kategoriye katkı verebilir).
        var contributions = new List<(string Category, Guid UnitId, decimal Amount)>();
        foreach (var source in _sources.OrderBy(s => s.Order))
            foreach (var c in await source.GetAsync(companyId, branchId, filter.AsOf))
                if (c.Amount != 0m)
                    contributions.Add((c.Category, c.UnitId, c.Amount));

        var result = new BalanceSheetReportResultDto
        {
            Scope      = filter.Scope,
            BaseUnitId = baseUnitId,
            AsOf       = filter.AsOf,
        };

        var unitCodes = await UnitCodesAsync(contributions.Select(c => c.UnitId).Append(baseUnitId));
        result.BaseCurrencyCode = unitCodes.GetValueOrDefault(baseUnitId) ?? string.Empty;

        if (contributions.Count == 0)
            return result;

        // ② Değerleme: base'e re-base'li efektifler (pozisyonla aynı servis; base÷base=1). AsOf geçilir →
        //    geçmiş tarihli bilanço O TARİHTEKİ kurla değerlenir (bugünün kuruyla DEĞİL); bugün ise canlı/güncel.
        var valuation = (await _pricing.GetValuationByBaseAsync(baseUnitId, filter.AsOf)).ToDictionary(v => v.Id);

        // TAKOZ pseudo-birim değerlemesi: raporsuz takoz gramı, HAS'a Carpan(varsayılan milyem) ile çevrilip
        // HAS'ın base-değeriyle değerlenir (legacy BakiyeKodlari: TAKOZ→HAS × Carpan). HAS=base ise birebir.
        var hasUnitId = await HasUnitIdAsync();
        var hasRate   = hasUnitId != Guid.Empty && valuation.TryGetValue(hasUnitId, out var hv) ? hv.Buy : 0m;

        // ③ Detay satırları (kategori×birim → base'e değerlenmiş Net; ERPPRO alış kuru).
        foreach (var c in contributions)
        {
            if (c.UnitId == BullionConsts.PseudoUnitId)
            {
                var takozRate = BullionConsts.DefaultCarpan * hasRate;   // 1 TAKOZ gram = Carpan HAS × HAS-base-kuru
                result.Rows.Add(new BalanceSheetDetailRowDto
                {
                    Category      = c.Category,
                    UnitId        = c.UnitId,
                    UnitCode      = CurrencyUnitCode.Bullion,            // "TAKOZ" (CurrencyUnit'te yok — özel-durum)
                    Amount        = c.Amount,
                    ValuationRate = takozRate,
                    Net           = c.Amount * takozRate,
                    MissingRate   = hasRate == 0m,                       // HAS değerlenemezse TAKOZ da değerlenemez
                });
                continue;
            }

            valuation.TryGetValue(c.UnitId, out var val);
            result.Rows.Add(new BalanceSheetDetailRowDto
            {
                Category      = c.Category,
                UnitId        = c.UnitId,
                UnitCode      = unitCodes.GetValueOrDefault(c.UnitId),
                Amount        = c.Amount,
                ValuationRate = val?.Buy ?? 0m,
                Net           = val == null ? 0m : c.Amount * val.Buy,
                MissingRate   = val == null,
            });
        }
        result.Rows = result.Rows.OrderBy(r => r.Category).ThenBy(r => r.UnitCode).ToList();

        // ④ Kategori toplamları + ⑤ TOPLAM (net varlık = TOPLAM'a giren kategoriler).
        result.CategoryTotals = result.Rows
            .GroupBy(r => r.Category)
            .Select(g => new BalanceSheetCategoryTotalDto
            {
                Category      = g.Key,
                Net           = g.Sum(r => r.Net),
                CountsInTotal = BalanceSheetCategory.CountsInTotal(g.Key),
            })
            .OrderBy(t => t.Category)
            .ToList();

        result.Total = result.CategoryTotals.Where(t => t.CountsInTotal).Sum(t => t.Net);

        return result;
    }

    /// <summary>
    /// Bilançoyu HESAPLA + DONDUR (ERPPRO <c>SaveAll</c> paritesi): <see cref="ComputeAsync"/> çıktısını aynı
    /// (Scope, CompanyId, BranchId, AsOfDate) kapsamında idempotent <b>sil + yeniden yaz</b> ile persist eder.
    /// CompanyId <see cref="ICurrentCompany"/>'den zorlanır (ComputeAsync ile aynı; sızıntı önleme). MissingRate
    /// satırları DA yazılır (ValuationRate=0/Net=0 ile — "o tarihte kur yoktu" bilgisi korunur). Sonuç DTO'su döner.
    /// </summary>
    public virtual async Task<BalanceSheetReportResultDto> SaveAsync(BalanceSheetReportFilterDto filter)
    {
        var result = await ComputeAsync(filter);

        // Working şirket yoksa (host/API) ComputeAsync boş döner → persist edecek bir şey yok.
        if (_currentCompany.Id is not { } companyId)
        {
            return result;
        }

        // Kapsam ComputeAsync ile AYNI çözümlenir (Branch scope + şirkete ait olmayan şube → konsolideye düşer).
        var branchId = await ResolveScopedBranchIdAsync(filter.Scope, filter.BranchId, companyId);

        var asOfDate = filter.AsOf.Date;

        // ① İdempotent: aynı gün + kapsam eski snapshot satırlarını HARD sil (ERPPRO SaveAll paritesi; snapshot
        // türetilmiş yeniden-yazım verisidir → soft-delete geçmişi tutmanın değeri yok, sadece şişme). DeleteDirectAsync
        // = EF ExecuteDelete: soft-delete bypass + tek SQL (BalanceLedgerSynchronizer ile aynı desen). asOfDate DELETE ve
        // INSERT'te AYNI normalize değer (.Date) → gün-only karşılaştırma tutarlı, idempotency korunur.
        await _snapshots.DeleteDirectAsync(
            s => s.CompanyId == companyId
              && s.Scope == filter.Scope
              && s.BranchId == branchId
              && s.AsOfDate == asOfDate);

        // ② ComputeAsync detay satırlarını dondurulmuş snapshot olarak yaz (MissingRate satırları da; Net=0).
        if (result.Rows.Count > 0)
        {
            var toInsert = result.Rows.Select(r => new BalanceSheetSnapshot(
                filter.Scope,
                companyId,
                branchId,
                asOfDate,
                r.Category,
                r.UnitId,
                r.Amount,
                r.ValuationRate,
                r.Net,
                result.BaseUnitId,
                result.BaseCurrencyCode)).ToList();

            await _snapshots.InsertManyAsync(toInsert, autoSave: true);
        }

        return result;
    }

    /// <summary>
    /// DÖNEM SIFIRLA (minimal, ERPPRO <c>FrmBilancoSifirla</c> kısmi muadili): kapsamdaki şube(ler)in
    /// <see cref="Branch.ProfitResetDate"/>'ini <c>filter.AsOf</c>'a ilerletir (P&L cari dönemi buradan başlar) +
    /// <see cref="SaveAsync"/> ile snapshot dondurur. Bu virtual app-service metodu TEK UnitOfWork'te çalışır (ABP UoW
    /// interceptor) → şube güncellemesi + snapshot yazımı ATOMİK commit olur (ERPPRO SaveAll + RevCostDate update atomik).
    /// CompanyId ICurrentCompany'den zorlanır. Branch scope → o şube; Company scope → şirketin TÜM şubeleri. RESMİ GL
    /// devir/prim postalaması YOK (net-varlık/TOPLAM zaten P&L içermez; devir/prim ayrı faz).
    /// </summary>
    public virtual async Task<BalanceSheetReportResultDto> ResetProfitPeriodAsync(BalanceSheetReportFilterDto filter)
    {
        // Working şirket yoksa (host/API) işlem yok — yalnız compute döner (SaveAsync ile tutarlı guard).
        if (_currentCompany.Id is not { } companyId)
        {
            return await ComputeAsync(filter);
        }

        // Kapsam ComputeAsync/SaveAsync ile AYNI: Branch scope + şirkete ait olmayan şube → konsolideye düşer.
        var branchId = await ResolveScopedBranchIdAsync(filter.Scope, filter.BranchId, companyId);

        var resetDate = filter.AsOf.Date;

        // Kapsamdaki şube(ler)in ProfitResetDate'ini ilerlet: Branch scope → o şube; Company scope → şirketin TÜM şubeleri.
        var bq = await _branches.GetQueryableAsync();
        var targets = await AsyncExecuter.ToListAsync(
            bq.Where(b => b.CompanyId == companyId && (branchId == null || b.Id == branchId)));
        foreach (var branch in targets)
        {
            branch.SetProfitResetDate(resetDate);
            await _branches.UpdateAsync(branch, autoSave: true);
        }

        // Snapshot'ı AYNI UoW'de dondur (atomik): SaveAsync reuse — kesme artık ilerletilmiş ProfitResetDate'i yansıtır.
        return await SaveAsync(filter);
    }

    /// <summary>
    /// Kaydedilmiş bilanço snapshot'larının GEÇMİŞ listesi (ERPPRO <c>BilancoListesi.Load</c> paritesi):
    /// scope+company (ICurrentCompany zorlanır) filtreli snapshot'ları oku → AsOfDate bazında PIVOT (kategori→Net) →
    /// TOPLAM (CountsInTotal kategoriler) → tarih ARTAN running türetim (DEVIR=önceki TOPLAM · KARZARAR=TOPLAM−DEVIR ·
    /// MASRAF=Expense+Income · GUNLUK=MASRAF delta · KURFARKI=gün-aşırı yeniden değerleme). Running in-memory
    /// (BilancoDevirleri tablosu OKUNMAZ).
    /// <para>KURFARKI (1b-3, ERPPRO <c>GetKurFarki</c> paritesi): ardışık snapshot çifti (i-1, i) için BİRİM bazında
    /// <c>Fark = Σ_unit [ row[i-1].Amount(unit) × row[i].ValuationRate(unit) − row[i-1].Net(unit) ]</c>, sonraki güne
    /// (row[i]) iliştirilir (ERPPRO T-1 semantiği; "önceki gün" = önceki SNAPSHOT satırı, takvim -1 DEĞİL). Expense/Income
    /// ve MissingRate (donuk rate=0) satırları HARİÇ. TOPLAM DIŞI (ayrı kolon). Sonraki rate cinsi = ValuationRate (marjlı
    /// re-base) → donuk Net ile aynı cins, temiz FX farkı. row[0].KurFarki=0 (öncesi yok).</para>
    /// </summary>
    public virtual async Task<BalanceSheetSnapshotListDto> GetSnapshotListAsync(BalanceSheetSnapshotListRequestDto request)
    {
        var dto = new BalanceSheetSnapshotListDto();

        // Sızıntı önleme: working şirket yoksa (host/API) boş.
        if (_currentCompany.Id is not { } companyId)
            return dto;

        // Kapsam ComputeAsync/SaveAsync ile AYNI: Branch scope + şirkete ait olmayan şube → konsolideye düşer.
        var branchId = await ResolveScopedBranchIdAsync(request.Scope, request.BranchId, companyId);

        // Birim-detay DA çekilir: KURFARKI birim (UnitId) bazlı yeniden değerleme gerektirir (Amount × sonraki ValuationRate).
        var q = await _snapshots.GetQueryableAsync();
        var snapshotRows = await AsyncExecuter.ToListAsync(
            q.Where(s => s.CompanyId == companyId
                      && s.Scope == request.Scope
                      && s.BranchId == branchId)
             .Select(s => new
             {
                 s.AsOfDate, s.BranchId, s.Category, s.UnitId, s.Amount, s.ValuationRate, s.Net, s.BaseCurrencyCode
             }));

        if (snapshotRows.Count == 0)
            return dto;

        // Saf-hesap girdisine map (EF projeksiyonu DEĞİŞMEDİ; pivot/running SnapshotPivotBuilder'da).
        var rows = snapshotRows
            .Select(r => new SnapshotPivotBuilder.SnapshotRow(
                r.AsOfDate, r.BranchId, r.Category, r.UnitId, r.Amount, r.ValuationRate, r.Net, r.BaseCurrencyCode))
            .ToList();

        // Görünen kategori anahtarları (kolon başlıkları); TOPLAM sırası sabit.
        dto.Categories = rows.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();

        // Şube kodları (Company scope'ta konsolide → boş). BranchId null olmayan snapshot'lar için çöz.
        var branchIds = rows.Where(r => r.BranchId != null).Select(r => r.BranchId!.Value).Distinct().ToList();
        var branchCodes = branchIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await AsyncExecuter.ToListAsync(
                (await _branches.GetQueryableAsync()).Where(b => branchIds.Contains(b.Id)).Select(b => new { b.Id, b.Code })))
              .ToDictionary(b => b.Id, b => b.Code);

        dto.Rows = SnapshotPivotBuilder.Build(rows, request.Scope, branchCodes);
        return dto;
    }

    /// <summary>
    /// Kapsam çözümü (Compute/Save/ResetProfitPeriod/GetSnapshotList ORTAK): Company scope → <c>null</c> (konsolide);
    /// Branch scope → istenen şube, şube yoksa ya da working şirkete ait değilse konsolideye (<c>null</c>) düşer.
    /// </summary>
    private async Task<Guid?> ResolveScopedBranchIdAsync(BalanceSheetScope scope, Guid? requestedBranchId, Guid companyId)
    {
        Guid? branchId = scope == BalanceSheetScope.Branch ? requestedBranchId : null;
        if (branchId is { } bid)
        {
            var branch = await _branches.FindAsync(bid);
            if (branch is null || branch.CompanyId != companyId)
            {
                branchId = null;
            }
        }

        return branchId;
    }

    /// <summary>Bilanço birimi: branch base'i (boş Guid ise şirket base'ine düşer); şube yoksa şirket base'i.</summary>
    private async Task<Guid> ResolveBaseUnitAsync(Guid companyId, Guid? branchId)
    {
        if (branchId is { } bid)
        {
            var branch = await _branches.FindAsync(bid);
            if (branch != null && branch.BaseCurrencyUnitId != Guid.Empty)
                return branch.BaseCurrencyUnitId;
        }
        var company = await _companies.FindAsync(companyId);
        return company?.BaseCurrencyUnitId ?? Guid.Empty;
    }

    /// <summary>Birim kodları (CurrencyUnit global → tenant filtresi kapalı).</summary>
    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();

        using (DataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _units.GetQueryableAsync())
                    .Where(u => idList.Contains(u.Id))
                    .Select(u => new { u.Id, u.Code }));
            return rows.ToDictionary(x => x.Id, x => x.Code);
        }
    }

    /// <summary>HAS biriminin Id'si — TAKOZ pseudo-birim değerlemesi için (tenant filtresi kapalı; yoksa Guid.Empty).</summary>
    private async Task<Guid> HasUnitIdAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _units.GetQueryableAsync())
                    .Where(u => u.Code == CurrencyUnitCode.HAS)
                    .Select(u => u.Id));
        }
    }
}
