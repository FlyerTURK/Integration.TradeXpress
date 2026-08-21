using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Reports;

/// <summary>
/// Mamül (<see cref="ProcessType.Good"/>) stok ve hareket raporları — <b>perakende adet (Quantity) +
/// miktar (Amount)</b> esaslıdır. Maden'den SADELEŞTİRME: milyem/HAS/işçilik YOK, Rezervasyon YOK,
/// para (PayTotal) rapor dışı. Birim = mamülün <see cref="Good.StockUnitCode"/>'u (voucher satırında
/// <see cref="VoucherLine.MainUnitId"/> mamülde daima boş → CurrencyUnit birim-lookup'ı KULLANILMAZ).
/// <list type="bullet">
///   <item>Etki = <c>±Quantity</c> ve <c>±Amount</c>; işaret Giriş(Inbound)→+, Çıkış(Outbound)→−.
///   isInflow = <c>Direction.IsInflow()</c>.</item>
///   <item>Stok gruplama = (CommodityId, CommodityCode, VariantId, VariantCode); yürüyen bakiye = (CommodityId, VariantId).</item>
///   <item>Source kolonu ödeme tipini bilgi amaçlı gösterir (Normal / Peşin / Bedelli / İade / Emanet / Miktar).</item>
/// </list>
/// Şirket sızıntı-önleme: rapor DAİMA çalışılan şirketle sınırlı (<see cref="ICurrentCompany"/>); yoksa (host/API) boş döner.
/// </summary>
[Authorize(TradeXpressPermissions.Reports.Good)]
public class GoodReportAppService : TradeXpressAppService, IGoodReportAppService
{
    private readonly IRepository<Voucher, Guid> _voucherRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IDataFilter _dataFilter;

    public GoodReportAppService(
        IRepository<Voucher, Guid> voucherRepository,
        IRepository<Vault, Guid> vaultRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<Good, Guid> goodRepository,
        IDataFilter dataFilter)
    {
        _voucherRepository    = voucherRepository;
        _vaultRepository      = vaultRepository;
        _branchRepository     = branchRepository;
        _companyRepository    = companyRepository;
        _subAccountRepository = subAccountRepository;
        _goodRepository       = goodRepository;
        _dataFilter           = dataFilter;
    }

    /// <summary>Bir fiziksel mamül hareketi: adet (Quantity) + miktar (Amount), her ikisi de işaretli etkiyle.
    /// Source ödeme tipini bilgi amaçlı taşır; para (PayTotal) rapora girmez.</summary>
    private sealed record GoodLeg(
        decimal EffectQty, decimal EffectAmount, string Source,
        decimal Quantity, decimal Amount,
        Guid? CommodityId, string? CommodityCode, Guid? VariantId, string? VariantCode,
        ProcessPaymentType? PaymentType,
        DateTime VoucherDate, long VoucherNumber, ProcessType ProcessType, ProcessDirectionType Direction,
        Guid? VaultId, Guid CompanyId, Guid BranchId, Guid? SubAccountId,
        string? Description, DateTime CreationTime, Guid LineId);

    // ────────────────────────────────────────────────────────────────────────────────
    //  Stok
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual async Task<List<GoodStockRowDto>> GetStockAsync(GoodReportFilterDto filter)
    {
        var legs = await QueryLegsAsync(filter, dateFiltered: false);

        // Grupla: mamül + varyant. Birim (StockUnitCode) mamül seviyesinde → anahtarda değil, sonradan doldurulur.
        //
        // REZERVASYON AYRIŞTIRMASI (2026-08-05): rezervasyon leg'leri fiziksel Net'e GİRMEZ, ayrı sayaçta
        // toplanır. Aritmetik Metal raporuyla ORTAK (ReservationSplit) — kopyalanan bir ayrım zamanla
        // birbirinden ayrışır ve ayrışma sessiz olur.
        var grouped = legs
            .GroupBy(x => new { x.CommodityId, x.CommodityCode, x.VariantId, x.VariantCode })
            .Select(g =>
            {
                var totals = ReservationSplit.Compute(g.Select(x => new ReservationLeg(
                    x.PaymentType == ProcessPaymentType.Reservation, x.EffectAmount, x.EffectQty)));

                return new GoodStockRowDto
                {
                    GoodId      = g.Key.CommodityId,
                    GoodCode    = g.Key.CommodityCode,
                    VariantId   = g.Key.VariantId,
                    VariantCode = g.Key.VariantCode,
                    InQuantity  = totals.InQuantity,
                    OutQuantity = totals.OutQuantity,
                    NetQuantity = totals.NetQuantity,
                    InAmount    = totals.InAmount,
                    OutAmount   = totals.OutAmount,
                    NetAmount   = totals.NetAmount,

                    ReservedOutQuantity = totals.ReservedOutQuantity,
                    ReservedOutAmount   = totals.ReservedOutAmount,
                    ReservedInQuantity  = totals.ReservedInQuantity,
                    ReservedInAmount    = totals.ReservedInAmount,
                    AvailableQuantity   = totals.AvailableQuantity,
                    AvailableAmount     = totals.AvailableAmount,
                };
            })
            .ToList();

        // Mamül adı + stok birimi — per-GOOD batch lookup (varyant/satır değil; aynı sözlük).
        var goodInfo = await GoodInfoAsync(grouped.Where(r => r.GoodId != null).Select(r => r.GoodId!.Value));
        foreach (var r in grouped)
        {
            if (r.GoodId is { } gid && goodInfo.TryGetValue(gid, out var info))
            {
                r.GoodName = info.Name;
                r.UnitCode = info.StockUnitCode;
            }
        }

        return grouped.OrderBy(r => r.GoodCode).ThenBy(r => r.VariantCode).ToList();
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Hareketler
    // ────────────────────────────────────────────────────────────────────────────────

    public virtual async Task<List<GoodMovementRowDto>> GetMovementsAsync(GoodReportFilterDto filter)
    {
        // Dönem içi leg'ler (tarih filtreli)
        var legs = (await QueryLegsAsync(filter, dateFiltered: true))
            .OrderBy(x => x.VoucherDate).ThenBy(x => x.CreationTime).ThenBy(x => x.LineId)
            .ToList();

        // Devreden: başlangıç tarihinden önceki tüm birikim (aynı (mamül, varyant) anahtarı)
        var carryLegs = await QueryLegsAsync(filter, dateFiltered: false,
            endExclusiveOverride: filter.Start.Date);

        var vaultCodes   = await CodeMapAsync(_vaultRepository,      legs.Where(x => x.VaultId      != null).Select(x => x.VaultId!.Value),      x => x.Id, x => x.Code);
        var branchCodes  = await CodeMapAsync(_branchRepository,     legs.Select(x => x.BranchId),                                                x => x.Id, x => x.Code);
        var companyCodes = await CodeMapAsync(_companyRepository,    legs.Select(x => x.CompanyId),                                               x => x.Id, x => x.Code);
        var subCodes     = await CodeMapAsync(_subAccountRepository, legs.Where(x => x.SubAccountId != null).Select(x => x.SubAccountId!.Value), x => x.Id, x => x.Code);

        // Mamül stok birimi (StockUnitCode) — devreden + dönem tüm mamülleri (per-GOOD).
        var allLegs  = legs.Concat(carryLegs).ToList();
        var goodInfo = await GoodInfoAsync(allLegs.Where(x => x.CommodityId != null).Select(x => x.CommodityId!.Value));

        var result     = new List<GoodMovementRowDto>();
        var running    = new Dictionary<(Guid? CommodityId, Guid? VariantId), decimal>();   // Quantity running
        var runningAmt = new Dictionary<(Guid? CommodityId, Guid? VariantId), decimal>();   // Amount running

        // Devreden satırları — (mamül, varyant) bazında.
        // REZERVASYON KÜMÜLATİFE KATILMAZ (2026-08-05): eskiden "Rezervasyon YOK" varsayımıyla tüm leg'ler
        // toplanıyordu; sipariş rezervasyonu Good'u da kapsayınca bu varsayım devreden bakiyeyi ŞİŞİRİRDİ.
        // Metal raporundaki davranışla hizalandı.
        foreach (var g in carryLegs.GroupBy(x => (x.CommodityId, x.VariantId)))
        {
            var physical = g.Where(x => x.PaymentType != ProcessPaymentType.Reservation).ToList();
            var carryQty = physical.Sum(x => x.EffectQty);
            var carryAmt = physical.Sum(x => x.EffectAmount);
            running[g.Key]    = carryQty;
            runningAmt[g.Key] = carryAmt;
            if (carryQty != 0m || carryAmt != 0m)
            {
                var first = g.First();
                result.Add(new GoodMovementRowDto
                {
                    VoucherDate    = filter.Start.Date,
                    IsCarryForward = true,
                    Source         = "Devreden",
                    CommodityCode  = first.CommodityCode,
                    VariantCode    = first.VariantCode,
                    UnitCode       = first.CommodityId is { } cid ? goodInfo.GetValueOrDefault(cid).StockUnitCode : null,
                    Effect         = carryQty,
                    RunningBalance = carryQty,
                    EffectAmount   = carryAmt,
                    RunningAmount  = carryAmt,
                });
            }
        }

        // Dönem hareketleri — yürüyen bakiye (mamül, varyant) bazında.
        foreach (var x in legs)
        {
            var key = (x.CommodityId, x.VariantId);
            running.TryGetValue(key, out var prevQty);
            runningAmt.TryGetValue(key, out var prevAmt);

            // Rezervasyon satırı GÖRÜNÜR ama bakiyeyi HAREKET ETTİRMEZ (Metal raporuyla aynı kural).
            var isReservation = x.PaymentType == ProcessPaymentType.Reservation;
            var rbQty = prevQty + (isReservation ? 0m : x.EffectQty);
            var rbAmt = prevAmt + (isReservation ? 0m : x.EffectAmount);
            running[key]    = rbQty;
            runningAmt[key] = rbAmt;

            result.Add(new GoodMovementRowDto
            {
                VoucherDate    = x.VoucherDate,
                VoucherNumber  = x.VoucherNumber,
                ProcessType    = x.ProcessType,
                ProcessCode    = VoucherProcessCode.Of(x.ProcessType, x.Direction, x.PaymentType),
                IsReservation  = isReservation,
                Source         = x.Source,
                CompanyCode    = companyCodes.GetValueOrDefault(x.CompanyId),
                BranchCode     = branchCodes.GetValueOrDefault(x.BranchId),
                VaultCode      = x.VaultId is { } v ? vaultCodes.GetValueOrDefault(v) : null,
                SubAccountCode = x.SubAccountId is { } s ? subCodes.GetValueOrDefault(s) : null,
                Direction      = x.Direction,
                CommodityCode  = x.CommodityCode,
                VariantCode    = x.VariantCode,
                UnitCode       = x.CommodityId is { } cid ? goodInfo.GetValueOrDefault(cid).StockUnitCode : null,
                Quantity       = x.Quantity,
                Amount         = x.Amount,
                Effect         = x.EffectQty,
                RunningBalance = rbQty,
                EffectAmount   = x.EffectAmount,
                RunningAmount  = rbAmt,
                Description    = x.Description,
            });
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Ortak sorgu — mamül voucher satırlarından leg'leri üretir
    // ────────────────────────────────────────────────────────────────────────────────

    private async Task<List<GoodLeg>> QueryLegsAsync(GoodReportFilterDto filter, bool dateFiltered,
        DateTime? endExclusiveOverride = null)
    {
        // SIZINTI ÖNLEME: rapor DAİMA çalışılan şirketle sınırlı (ICurrentCompany). Yoksa (host/API) boş.
        if (LazyServiceProvider.LazyGetRequiredService<ICurrentCompany>().Id is not { } companyId)
            return new List<GoodLeg>();

        var start        = filter.Start.Date;
        var endExclusive = endExclusiveOverride ?? filter.End.Date.AddDays(1);

        var q = await _voucherRepository.GetQueryableAsync();
        var rows = await AsyncExecuter.ToListAsync(
            from v in q
            where v.CompanyId == companyId
               && (filter.BranchId == null || v.BranchId == filter.BranchId)
               && (filter.VaultId  == null || v.VaultId  == filter.VaultId)
               && (!dateFiltered && endExclusiveOverride == null
                   || (dateFiltered         && v.VoucherDate >= start && v.VoucherDate < endExclusive)
                   || (endExclusiveOverride != null            && v.VoucherDate < endExclusive))
            from l in v.Lines
            where !l.IsDeleted
               && l.Type == ProcessType.Good
               && (filter.GoodId    == null || l.CommodityId == filter.GoodId)
               && (filter.VariantId == null || l.VariantId   == filter.VariantId)
            select new
            {
                v.VoucherDate, v.VoucherNumber, v.VaultId, v.CompanyId, v.BranchId, v.SubAccountId,
                l.Type, l.PaymentType, l.Direction,
                l.CommodityId, l.CommodityCode, l.VariantId, l.VariantCode, l.Amount, l.Quantity,
                l.Description, l.CreationTime, l.Id,
            });

        // Source → ödeme tipi Türkçesi (bilgi amaçlı). Mamülde Rezervasyon YOK → o dal atlandı.
        static string PaymentSource(ProcessPaymentType? t) => t switch
        {
            ProcessPaymentType.Normal       => "Normal",
            ProcessPaymentType.WithCash     => "Peşin",
            ProcessPaymentType.WithCurrency => "Bedelli",
            ProcessPaymentType.Return       => "İade",
            ProcessPaymentType.Consignment  => "Emanet",
            ProcessPaymentType.WithUnit     => "Miktar",
            _                               => "Diğer",
        };

        var legs = new List<GoodLeg>(rows.Count);
        foreach (var r in rows)
        {
            // Perakende: adet VEYA miktar hareket eder. İkisi de 0 ise satır fiziksel etki taşımaz → atla.
            if (r.Quantity == 0m && r.Amount == 0m) continue;

            var sign   = r.Direction.IsInflow() ? 1m : -1m;   // Giriş +, Çıkış −
            var source = PaymentSource(r.PaymentType);

            legs.Add(new GoodLeg(
                sign * r.Quantity, sign * r.Amount, source,
                r.Quantity, r.Amount,
                r.CommodityId, r.CommodityCode, r.VariantId, r.VariantCode, r.PaymentType,
                r.VoucherDate, r.VoucherNumber, r.Type, r.Direction,
                r.VaultId, r.CompanyId, r.BranchId, r.SubAccountId, r.Description, r.CreationTime, r.Id));
        }

        return legs;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Yardımcı
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Mamül adı + stok birimi — id-bazlı batch lookup. id'ler çalışılan şirketin kendi fişlerinden
    /// geldiği için sızıntı yok → holding-host / şirket-özel tüm mamülleri çözmek adına ICompanyScoped devre-dışı
    /// (IMultiTenant AÇIK kalır — tenant sınırı korunur).</summary>
    private async Task<Dictionary<Guid, (string Name, string? StockUnitCode)>> GoodInfoAsync(IEnumerable<Guid> ids)
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();

        using (_dataFilter.Disable<ICompanyScoped>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _goodRepository.GetQueryableAsync())
                    .Where(g => idList.Contains(g.Id))
                    .Select(g => new { g.Id, g.Name, g.StockUnitCode }));
            return rows.ToDictionary(g => g.Id, g => (g.Name, g.StockUnitCode));
        }
    }

    private async Task<Dictionary<Guid, string>> CodeMapAsync<T>(
        IRepository<T, Guid> repo, IEnumerable<Guid> ids, Func<T, Guid> keyOf, Func<T, string> codeOf)
        where T : class, IEntity<Guid>
    {
        var idList = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (idList.Count == 0) return new();
        var rows = await AsyncExecuter.ToListAsync((await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows.ToDictionary(keyOf, codeOf);
    }
}
