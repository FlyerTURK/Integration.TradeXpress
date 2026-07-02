using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Metals;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Maden stok ve hareket raporları — <b>fiziksel maden miktarı</b> (Amount @ MainUnit) esaslıdır.
/// Ödeme tipi (Peşin / Bedelli / Normal vb.) ve fiyat bilgisi (PayTotal / PayUnit) stok dışıdır;
/// her ödeme tipinde yalnız tek bacak üretilir: kaç birim maden girdi veya çıktı.
/// <list type="bullet">
///   <item>Etki = <c>±Amount</c> (<see cref="VoucherLine.Amount"/>), birim = <see cref="VoucherLine.MainUnitId"/>.</item>
///   <item>Source kolonu ödeme tipini bilgi amaçlı gösterir (Normal / Peşin / Bedelli / İade / Emanet / Miktar).</item>
/// </list>
/// İşaret: Giriş(Inbound) → +, Çıkış(Outbound) → −. isInflow = <c>(int)Direction % 2 == 0</c>.
/// </summary>
[Authorize]
public class MetalReportAppService : TradeXpressAppService, IMetalReportAppService
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IDataFilter _dataFilter;

    public MetalReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Metal, Guid> metalRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository    = voucherRepository;
        _vaultRepository      = vaultRepository;
        _branchRepository     = branchRepository;
        _companyRepository    = companyRepository;
        _unitRepository       = unitRepository;
        _subAccountRepository = subAccountRepository;
        _metalRepository      = metalRepository;
        _dataFilter           = dataFilter;
    }

    /// <summary>
    /// Bir fiziksel maden hareketi: Amount @ MainUnit + Quantity (adet). Source, ödeme tipini bilgi amaçlı taşır.
    /// </summary>
    private sealed record MetalLeg(
        Guid UnitId, decimal Effect, decimal EffectQty, string Source,
        decimal Amount, decimal Quantity, decimal Factor,
        Guid? CommodityId, string? CommodityCode, ProcessPaymentType? PaymentType,
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction,
        Guid? VaultId, Guid CompanyId, Guid BranchId, Guid? SubAccountId,
        string? Description, DateTime CreationTime, Guid LineId);

    // ────────────────────────────────────────────────────────────────────────────────
    //  Stok
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual async Task<List<MetalStockRowDto>> GetStockAsync(MetalReportFilterDto filter)
    {
        var legs = await QueryLegsAsync(filter, dateFiltered: false);

        var grouped = legs
            .GroupBy(x => new { x.CommodityId, x.CommodityCode, x.UnitId })
            .Select(g => new MetalStockRowDto
            {
                MetalId     = g.Key.CommodityId,
                MetalCode   = g.Key.CommodityCode,
                UnitId      = g.Key.UnitId,
                InAmount    = g.Where(x => x.Effect    > 0).Sum(x => x.Effect),
                OutAmount   = g.Where(x => x.Effect    < 0).Sum(x => -x.Effect),
                NetAmount   = g.Sum(x => x.Effect),
                InQuantity  = g.Where(x => x.EffectQty > 0).Sum(x => x.EffectQty),
                OutQuantity = g.Where(x => x.EffectQty < 0).Sum(x => -x.EffectQty),
                NetQuantity = g.Sum(x => x.EffectQty),
            })
            .ToList();

        var unitCodes = await UnitCodesAsync(grouped.Select(r => r.UnitId));

        var metalIds = grouped.Where(r => r.MetalId != null).Select(r => r.MetalId!.Value).Distinct().ToList();
        var metalNames = new Dictionary<Guid, string>();
        if (metalIds.Count > 0)
        {
            var metalsList = await AsyncExecuter.ToListAsync(
                (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id))
            );
            metalNames = metalsList.ToDictionary(m => m.Id, m => m.Name);
        }

        foreach (var r in grouped)
        {
            r.UnitCode = unitCodes.GetValueOrDefault(r.UnitId);
            if (r.MetalId.HasValue)
            {
                r.MetalName = metalNames.GetValueOrDefault(r.MetalId.Value);
            }
        }
        return grouped.OrderBy(r => r.MetalCode).ToList();
    }

    /// <summary>
    /// Bilanço STOK(maden) için fiziksel maden holding'i: kapsam (şirket DAİMA ICurrentCompany'den) + branch/vault,
    /// asOfExclusive'den ÖNCE birikmiş net, MainUnit(maden birimi)-bazında. Bacak çıkarımı tek kaynakta
    /// (<see cref="QueryLegsAsync"/>; DRY). Net = FİZİKSEL holding (Amount @ MainUnit, tüm ödeme tipleri/Peşin dahil;
    /// + = firma o madeni tutar). Cari metal (ledger/BAKIYE) AYRI boyut → çift sayım değil, offset. Değerleme merkezde.
    /// </summary>
    public virtual async Task<Dictionary<Guid, decimal>> GetMetalNetByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive)
    {
        var legs = await QueryLegsAsync(
            new MetalReportFilterDto { BranchId = branchId, VaultId = vaultId },
            dateFiltered: false, endExclusiveOverride: asOfExclusive);
        // HAS İÇERİĞİ = Effect × Factor (= sign × Amount × Factor = sign × Total). HAM gram (Amount) DEĞİL —
        // MainUnit=HAS olduğundan değerleme HAS kuruyla yapılır; poster'ın cari'ye yazdığı Total ile BİREBİR offset.
        // (Bilanço HAS-değerlemesi için Total gerekir; GetStockAsync'in Amount'u adet/gram raporu içindir.)
        return legs.GroupBy(x => x.UnitId).ToDictionary(g => g.Key, g => g.Sum(x => x.Effect * x.Factor));
    }

    /// <summary>
    /// Bilanço İŞÇİLİK (Labor) için: <b>on-hand (satılmamış) madenin işçilik MALİYETİ</b> (SERMAYE/VARLIK). Maden Normal/İade/Emanet
    /// işçilik bacağı (PayUnit/PayTotal); maliyet **AĞIRLIKLI-ORTALAMA** (ERPPRO GetMadenMaliyeti paritesi): çıkış işçiliği SATIŞ fiyatıyla
    /// DEĞİL, giriş-MALİYETİYLE düşer → metal başına işçilik = Σ(giriş işçilik) × (on-hand miktar / Σ giriş miktar). Para-birimi bazında döner;
    /// merkez base'e (HAS) çevirir. <b>TOPLAM'a GİRER</b>; BAKİYE'deki işçilik cari'sini OFFSET eder. Hep ≥0 (varlık).
    /// ✅ ERPPRO paritesi: alış 2500 → İŞÇİLİK +2500 (break-even); alış 2500 + satış 2800 → İŞÇİLİK 0 (satıldı), kâr +300 BAKİYE'de (KAR).
    /// </summary>
    public virtual async Task<Dictionary<Guid, decimal>> GetMetalLaborByUnitAsync(Guid? branchId, Guid? vaultId, DateTime asOfExclusive)
    {
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<Guid, decimal>();

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && (vaultId == null || v.VaultId == vaultId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Metal
               && (l.PaymentType == ProcessPaymentType.Normal
                   || l.PaymentType == ProcessPaymentType.Return
                   || l.PaymentType == ProcessPaymentType.Consignment)
               && l.CommodityId != null && l.PayUnitId != null && l.PayTotal != 0m
            select new { CommodityId = l.CommodityId!.Value, PayUnitId = l.PayUnitId!.Value, l.Direction, l.PayTotal, l.Amount });

        // On-hand işçilik MALİYETİ (ağırlıklı-ortalama, cost-inventory): metal başına Σ(giriş işçilik) × (on-hand miktar / Σ giriş miktar).
        // Çıkış işçiliği maliyetle düşer → tüm stok satılınca işçilik 0, kâr BAKİYE'de görünür (ERPPRO GetMadenMaliyeti paritesi).
        var result = new Dictionary<Guid, decimal>();
        foreach (var g in rows.GroupBy(r => new { r.CommodityId, r.PayUnitId }))
        {
            var girisLabor = g.Where(r => ((int)r.Direction % 2) == 0).Sum(r => r.PayTotal);   // giriş işçilik maliyeti
            var girisQty   = g.Where(r => ((int)r.Direction % 2) == 0).Sum(r => r.Amount);      // giriş miktar (terazi/adet)
            var cikisQty   = g.Where(r => ((int)r.Direction % 2) != 0).Sum(r => r.Amount);      // çıkış miktar
            if (girisQty <= 0m) continue;
            var onHand = girisLabor * Math.Max(0m, girisQty - cikisQty) / girisQty;             // satılmamış kısmın işçilik maliyeti
            result[g.Key.PayUnitId] = result.GetValueOrDefault(g.Key.PayUnitId) + onHand;
        }
        return result;
    }

    /// <summary>DRILL — metal FİZİKSEL stok, COMMODITY (metal kodu) bazında, tek birim için (bilanço Stok popup). Net = Σ(Effect×Factor) @ unit.</summary>
    public virtual async Task<Dictionary<string, decimal>> GetMetalStockByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive)
    {
        var legs = await QueryLegsAsync(
            new MetalReportFilterDto { BranchId = branchId },
            dateFiltered: false, endExclusiveOverride: asOfExclusive);
        return legs.Where(x => x.UnitId == unitId)
            .GroupBy(x => x.CommodityCode ?? "?")
            .Select(g => new { Code = g.Key, Net = g.Sum(x => x.Effect * x.Factor) })
            .Where(x => x.Net != 0m)
            .ToDictionary(x => x.Code, x => x.Net);
    }

    /// <summary>DRILL — metal İŞÇİLİK maliyeti (on-hand), COMMODITY bazında, tek PayUnit için (bilanço İşçilik popup). GetMetalLaborByUnitAsync paritesi, metal-kodu kırılımı.</summary>
    public virtual async Task<Dictionary<string, decimal>> GetMetalLaborByCommodityAsync(Guid? branchId, Guid unitId, DateTime asOfExclusive)
    {
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new Dictionary<string, decimal>();

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (branchId == null || v.BranchId == branchId)
               && v.VoucherDate < asOfExclusive
            from l in v.Lines
            where !l.IsDeleted && l.Type == ProcessType.Metal
               && (l.PaymentType == ProcessPaymentType.Normal
                   || l.PaymentType == ProcessPaymentType.Return
                   || l.PaymentType == ProcessPaymentType.Consignment)
               && l.CommodityId != null && l.PayUnitId == unitId && l.PayTotal != 0m
            select new { l.CommodityCode, l.Direction, l.PayTotal, l.Amount });

        var result = new Dictionary<string, decimal>();
        foreach (var g in rows.GroupBy(r => r.CommodityCode ?? "?"))
        {
            var girisLabor = g.Where(r => ((int)r.Direction % 2) == 0).Sum(r => r.PayTotal);
            var girisQty   = g.Where(r => ((int)r.Direction % 2) == 0).Sum(r => r.Amount);
            var cikisQty   = g.Where(r => ((int)r.Direction % 2) != 0).Sum(r => r.Amount);
            if (girisQty <= 0m) continue;
            var onHand = girisLabor * Math.Max(0m, girisQty - cikisQty) / girisQty;
            if (onHand != 0m) result[g.Key] = result.GetValueOrDefault(g.Key) + onHand;
        }
        return result;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Hareketler
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual async Task<List<MetalMovementRowDto>> GetMovementsAsync(MetalReportFilterDto filter)
    {
        // Dönem içi bacaklar (tarih filtreli)
        var legs = (await QueryLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId)
            .ToList();

        // Devreden: başlangıç tarihinden önceki tüm birikim
        var carryLegs = await QueryLegsAsync(filter, dateFiltered: false,
            endExclusiveOverride: filter.Start.Date);

        var allLegs      = legs.Concat(carryLegs).ToList();
        var unitCodes    = await UnitCodesAsync(allLegs.Select(x => x.UnitId));
        var vaultCodes   = await CodeMapAsync(_vaultRepository,   legs.Where(x => x.VaultId      != null).Select(x => x.VaultId!.Value),      x => x.Id, x => x.Code);
        var branchCodes  = await CodeMapAsync(_branchRepository,  legs.Select(x => x.BranchId),                                                x => x.Id, x => x.Code);
        var companyCodes = await CodeMapAsync(_companyRepository, legs.Select(x => x.CompanyId),                                               x => x.Id, x => x.Code);
        var subCodes     = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        var result      = new List<MetalMovementRowDto>();
        var running     = new Dictionary<Guid, decimal>();   // Amount running
        var runningQty  = new Dictionary<Guid, decimal>();   // Quantity running

        // Devreden satırları — birim bazında
        foreach (var g in carryLegs.GroupBy(x => x.UnitId))
        {
            var carry    = g.Sum(x => x.Effect);
            var carryQty = g.Sum(x => x.EffectQty);
            running[g.Key]    = carry;
            runningQty[g.Key] = carryQty;
            if (carry != 0m || carryQty != 0m)
                result.Add(new MetalMovementRowDto
                {
                    VoucherDate    = filter.Start.Date,
                    IsCarryForward = true,
                    Source         = "Devreden",
                    UnitId         = g.Key,
                    UnitCode       = unitCodes.GetValueOrDefault(g.Key),
                    Effect         = carry,
                    RunningBalance = carry,
                    EffectQty      = carryQty,
                    RunningQty     = carryQty,
                });
        }

        // Dönem hareketleri
        foreach (var x in legs)
        {
            running.TryGetValue(x.UnitId, out var prev);
            runningQty.TryGetValue(x.UnitId, out var prevQty);
            var rb    = prev    + x.Effect;
            var rbQty = prevQty + x.EffectQty;
            running[x.UnitId]    = rb;
            runningQty[x.UnitId] = rbQty;

            result.Add(new MetalMovementRowDto
            {
                VoucherDate    = x.VoucherDate,
                VoucherNumber  = x.VoucherNumber,
                ProcessType    = x.ProcessType,
                ProcessCode    = VoucherProcessCode.Of(x.ProcessType, x.Direction, x.PaymentType),
                Source         = x.Source,
                CompanyCode    = companyCodes.GetValueOrDefault(x.CompanyId),
                BranchCode     = branchCodes.GetValueOrDefault(x.BranchId),
                VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
                SubAccountCode = x.SubAccountId is { } s ? subCodes.GetValueOrDefault(s) : null,
                Direction      = x.Direction,
                CommodityCode  = x.CommodityCode,
                UnitId         = x.UnitId,
                UnitCode       = unitCodes.GetValueOrDefault(x.UnitId),
                Quantity       = x.Quantity,
                Amount         = x.Amount,
                Factor         = x.Factor,
                Effect         = x.Effect,
                RunningBalance = rb,
                EffectQty      = x.EffectQty,
                RunningQty     = rbQty,
                Description    = x.Description,
            });
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Ortak sorgu — MetalBalancePoster mantığıyla bacakları üretir
    // ────────────────────────────────────────────────────────────────────────────────

    private async Task<List<MetalLeg>> QueryLegsAsync(MetalReportFilterDto filter, bool dateFiltered,
        DateTime? endExclusiveOverride = null)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<MetalLeg>();

        var start        = filter.Start.Date;
        var endExclusive = endExclusiveOverride ?? filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId  == null || v.BranchId  == filter.BranchId)
               && (filter.VaultId   == null || v.VaultId   == filter.VaultId)
               && (!dateFiltered && endExclusiveOverride == null
                   || (dateFiltered         && v.VoucherDate >= start && v.VoucherDate < endExclusive)
                   || (endExclusiveOverride != null            && v.VoucherDate < endExclusive))
            from l in v.Lines
            where !l.IsDeleted
               && l.Type == ProcessType.Metal
               // Peşin dahil: maden fiziksel olarak hareket eder; peşin yalnızca ödeme yöntemidir.
               && (filter.MetalId == null || l.CommodityId == filter.MetalId)
            select new
            {
                v.VoucherDate, v.VoucherNumber, v.VaultId, v.CompanyId, v.BranchId, v.SubAccountId,
                l.Type, l.PaymentType, l.Direction,
                l.MainUnitId, l.CommodityId, l.CommodityCode, l.Amount, l.Quantity, l.Factor,
                l.Description, l.CreationTime, l.Id,
            });

        // Tüm ödeme tiplerinde tek kural: fiziksel maden miktarı = Amount @ MainUnit.
        // Fiyat/bedel (PayTotal / PayUnit) stok dışıdır; hiç kullanılmaz.
        // Source → ödeme tipi Türkçesi (bilgi amaçlı).
        static string PaymentSource(ProcessPaymentType? t) => t switch
        {
            ProcessPaymentType.Normal      => "Normal",
            ProcessPaymentType.WithCash    => "Peşin",
            ProcessPaymentType.WithCurrency=> "Bedelli",
            ProcessPaymentType.Return      => "İade",
            ProcessPaymentType.Consignment => "Emanet",
            ProcessPaymentType.WithUnit    => "Miktar",
            _                              => "Diğer",
        };

        var legs = new List<MetalLeg>(rows.Count);
        foreach (var r in rows)
        {
            if (r.MainUnitId == Guid.Empty || r.Amount == 0m) continue;

            var sign   = (((int)r.Direction % 2) == 0) ? 1m : -1m;   // Giriş +, Çıkış −
            var source = PaymentSource(r.PaymentType);

            legs.Add(new MetalLeg(
                r.MainUnitId, sign * r.Amount, sign * r.Quantity, source,
                r.Amount, r.Quantity, r.Factor,
                r.CommodityId, r.CommodityCode, r.PaymentType,
                r.VoucherDate, r.VoucherNumber, r.Type, r.Direction,
                r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));
        }

        return legs;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Yardımcı
    // ────────────────────────────────────────────────────────────────────────────────

    private Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> ids)
        => CodeMapAsync(_unitRepository, ids, u => u.Id, u => u.Code, disableMultiTenant: true);

    private async Task<Dictionary<Guid, string>> CodeMapAsync<T>(
        IRepository<T, Guid> repo, IEnumerable<Guid> ids, Func<T, Guid> keyOf, Func<T, string> codeOf,
        bool disableMultiTenant = false)
        where T : class, Volo.Abp.Domain.Entities.IEntity<Guid>
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();
        if (disableMultiTenant)
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var r = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
                return r.ToDictionary(keyOf, codeOf);
            }
        var rows = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows.ToDictionary(keyOf, codeOf);
    }
}
