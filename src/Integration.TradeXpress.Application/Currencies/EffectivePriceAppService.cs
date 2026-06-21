using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Efektif fiyat motoru. Ham ExchangeRate (host, pivot TRY) üstüne <b>kademe</b> uygular:
/// host marjı → (viewer tenant ise) viewer marjı (append-only CurrencyUnitMargin'den güncel=en son).
/// Cross-tenant okuma için tenant filtresi disable; görünürlük açık predicate (host null + viewer).
///
/// <para><see cref="GetCurrentPricesAsync"/> = pivot (X/TRY) board verisi.
/// <see cref="GetValuationAsync"/> = aktif şirket base'ine re-base (DEĞERLEME; parite forex yönü AYRI).</para>
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnits.Default)]
public class EffectivePriceAppService : TradeXpressAppService, IEffectivePriceAppService
{
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly IRepository<CurrencyUnitMargin, Guid> _marginRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly ExchangeRateCacheService _cache;
    private readonly IDataFilter _dataFilter;

    public EffectivePriceAppService(
        IRepository<ExchangeRate, Guid> rateRepository,
        IRepository<CurrencyUnitMargin, Guid> marginRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Company, Guid> companyRepository,
        ExchangeRateCacheService cache,
        IDataFilter dataFilter)
    {
        _rateRepository = rateRepository;
        _marginRepository = marginRepository;
        _unitRepository = unitRepository;
        _companyRepository = companyRepository;
        _cache = cache;
        _dataFilter = dataFilter;
    }

    public virtual async Task<List<CurrentPriceDto>> GetCurrentPricesAsync()
    {
        var prices = await ComputeEffectiveAsync();
        return prices
            .Select(e => new CurrentPriceDto
            {
                Id = e.Unit.Id,
                CurrencyUnitCode = e.Unit.Code,
                CurrencyUnitName = e.Unit.Name,
                UnitType = e.Unit.Type,
                DisplayOrder = e.Unit.DisplayOrder,
                Buy = e.Eff.Buy,
                Sell = e.Eff.Sell,
                RawBuy = e.Raw.Buy,
                RawSell = e.Raw.Sell,
                MarginOnBuyType = e.AppliedBuy.Type,
                MarginOnBuyValue = e.AppliedBuy.Value,
                MarginOnSellType = e.AppliedSell.Type,
                MarginOnSellValue = e.AppliedSell.Value,
                GuardFired = e.Eff.GuardFired,
                RateDate = e.RateDate,
            })
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.CurrencyUnitCode).ToList();
    }

    public virtual async Task<List<ValuationPriceDto>> GetValuationAsync(Guid? companyId = null)
    {
        var company = await ResolveCompanyAsync(companyId);
        var prices = await ComputeEffectiveAsync();

        // Host / şirketsiz scope → pivot (TRY) değerleme, re-base YOK (identity). Host merkezi
        // operasyonları pivot'ta görür; şirket/base tenant'a aittir.
        if (company == null)
        {
            return prices
                .Select(e => new ValuationPriceDto
                {
                    Id = e.Unit.Id,
                    CurrencyUnitCode = e.Unit.Code,
                    CurrencyUnitName = e.Unit.Name,
                    UnitType = e.Unit.Type,
                    DisplayOrder = e.Unit.DisplayOrder,
                    Buy = e.Eff.Buy,
                    Sell = e.Eff.Sell,
                    BaseCurrencyCode = CurrencyUnitCode.TRY,
                    GuardFired = e.Eff.GuardFired,
                })
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.CurrencyUnitCode).ToList();
        }

        var baseCode = await GetCurrencyCodeAsync(company.BaseCurrencyUnitId);
        var byUnit = prices.ToDictionary(e => e.Unit.Id);

        // Base biriminin efektifi olmadan re-base yapılamaz (örn. feed gelmemiş USD).
        if (!byUnit.TryGetValue(company.BaseCurrencyUnitId, out var baseEff))
            return new List<ValuationPriceDto>();

        var result = new List<ValuationPriceDto>();
        foreach (var e in prices)
        {
            // Re-base (per-leg bölme) + guard (bid/ask ters dönerse takas → doğru çapraz).
            var rb = CurrencyPriceCalculator.ReBase(e.Eff, baseEff.Eff);
            var valued = CurrencyPriceCalculator.Guard(rb.Buy, rb.Sell);

            result.Add(new ValuationPriceDto
            {
                Id = e.Unit.Id,
                CurrencyUnitCode = e.Unit.Code,
                CurrencyUnitName = e.Unit.Name,
                UnitType = e.Unit.Type,
                DisplayOrder = e.Unit.DisplayOrder,
                Buy = valued.Buy,
                Sell = valued.Sell,
                BaseCurrencyCode = baseCode,
                GuardFired = valued.GuardFired || e.Eff.GuardFired,
            });
        }

        return result.OrderBy(p => p.DisplayOrder).ThenBy(p => p.CurrencyUnitCode).ToList();
    }

    // ── çekirdek: birim başına efektif (pivot) ──────────────────────────────────

    private sealed record EffPrice(CurrencyUnit Unit, CurrencyPrice Eff, CurrencyPrice Raw, DateTime RateDate,
        MarginSetting AppliedBuy, MarginSetting AppliedSell);

    private async Task<List<EffPrice>> ComputeEffectiveAsync()
    {
        var viewer = CurrentTenant.Id;

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.TenantId == null || u.TenantId == viewer));

            var rawRows = await AsyncExecuter.ToListAsync(
                (await _rateRepository.GetQueryableAsync()).Where(r => r.TenantId == null));
            var latestRaw = LatestBy(rawRows, r => r.CurrencyUnitId, r => r.RateDate, r => r.Id);

            var hostMarginRows = await AsyncExecuter.ToListAsync(
                (await _marginRepository.GetQueryableAsync()).Where(m => m.TenantId == null));
            var hostMargin = LatestBy(hostMarginRows, m => m.CurrencyUnitId, m => m.CreationTime, m => m.Id);

            Dictionary<Guid, CurrencyUnitMargin> viewerMargin = hostMargin;
            if (viewer != null)
            {
                var viewerRows = await AsyncExecuter.ToListAsync(
                    (await _marginRepository.GetQueryableAsync()).Where(m => m.TenantId == viewer));
                viewerMargin = LatestBy(viewerRows, m => m.CurrencyUnitId, m => m.CreationTime, m => m.Id);
            }

            // Canlı tick cache'i (worker her poll'da günceller; CANLI). Birim koduna göre.
            var live = _cache.GetAll();

            // Bağımlılık-sıralı çözüm: doğrudan (feed/DB) fiyatı olan birimler hemen; takip eden birimler
            // takip edilenin EFEKTİFİ hesaplandıktan SONRA türetilir. Tur tur ilerle (zincir A→B→C);
            // ilerleme durunca kalanlar çözülemez (fiyatsız ya da döngü) → atlanır. Cycle-safe.
            var byId = new Dictionary<Guid, EffPrice>();
            var pending = new List<CurrencyUnit>(units);
            bool progressed = true;
            while (progressed && pending.Count > 0)
            {
                progressed = false;
                var stillPending = new List<CurrencyUnit>();
                foreach (var unit in pending)
                {
                    // Ham fiyat: önce CANLI cache (sub-dakika), yoksa DB 15-dk snapshot, yoksa TAKİP türevi.
                    CurrencyPrice raw;
                    DateTime rateDate;
                    if (live.TryGetValue(unit.Code, out var q) && q.Buy > 0m && q.Sell > 0m)
                    {
                        raw = CurrencyPriceCalculator.Guard(q.Buy, q.Sell);
                        rateDate = q.UpdatedAtUtc ?? DateTime.UtcNow;
                    }
                    else if (latestRaw.TryGetValue(unit.Id, out var rate))
                    {
                        raw = new CurrencyPrice(rate.MarketPriceOnBuy, rate.MarketPriceOnSell, rate.GuardFired);
                        rateDate = rate.RateDate;
                    }
                    else if (unit.FollowingUnitId is { } parentId && unit.FollowingMargin is { } followingMargin)
                    {
                        // Takip edilen birimin EFEKTİFİ hazır değilse sonraki tura ertele.
                        if (!byId.TryGetValue(parentId, out var parent))
                        {
                            stillPending.Add(unit);
                            continue;
                        }
                        // Takip edilenin efektifine following margin (iki yana aynı) → bu birimin "ham"ı.
                        raw = CurrencyPriceCalculator.Guard(
                            followingMargin.Apply(parent.Eff.Buy),
                            followingMargin.Apply(parent.Eff.Sell));
                        rateDate = parent.RateDate;
                    }
                    else
                    {
                        // Ne canlı tick, ne DB rate, ne takip → kur bağlantısı YOK. null yerine VARSAYILAN 1/1
                        // (üstüne varsa kendi host/viewer marjı biner). Böylece fiyatsız birim listede boş geçmez.
                        raw = CurrencyPriceCalculator.Guard(1m, 1m);
                        rateDate = DateTime.UtcNow;
                    }

                    // Kademe katmanları: host marjı, sonra (tenant ise) viewer marjı.
                    (MarginSetting OnBuy, MarginSetting OnSell)? hostLayer =
                        hostMargin.TryGetValue(unit.Id, out var hm) ? (hm.MarginOnBuy, hm.MarginOnSell) : null;
                    (MarginSetting OnBuy, MarginSetting OnSell)? viewerLayer =
                        (viewer != null && viewerMargin.TryGetValue(unit.Id, out var vm)) ? (vm.MarginOnBuy, vm.MarginOnSell) : null;

                    var layers = new List<(MarginSetting, MarginSetting)>();
                    if (hostLayer is { } hl) layers.Add(hl);
                    if (viewerLayer is { } vl) layers.Add(vl);
                    var eff = CurrencyPriceCalculator.Cascade(raw, layers.ToArray());

                    // VERİ İZOLASYONU (kaynakta): TENANT, host'un HAM fiyatını ASLA görmez. Gösterilen "baz" =
                    // host EFEKTİFİ (raw + host marjı); gösterilen marj = viewer'ın KENDİ marjı (tenant marjı).
                    // HOST için baz = ham, marj = host'un kendi marjı. Böylece her zaman: baz · marj = nihai.
                    CurrencyPrice displayBase;
                    MarginSetting appliedBuy, appliedSell;
                    if (viewer == null)
                    {
                        displayBase = raw;
                        (appliedBuy, appliedSell) = hostLayer ?? (MarginSetting.Passthrough, MarginSetting.Passthrough);
                    }
                    else
                    {
                        displayBase = hostLayer is { } h
                            ? CurrencyPriceCalculator.ApplyLayer(raw, h.OnBuy, h.OnSell)
                            : raw;
                        (appliedBuy, appliedSell) = viewerLayer ?? (MarginSetting.Passthrough, MarginSetting.Passthrough);
                    }

                    byId[unit.Id] = new EffPrice(unit, eff, displayBase, rateDate, appliedBuy, appliedSell);
                    progressed = true;
                }
                pending = stillPending;
            }

            return byId.Values.ToList();
        }
    }

    /// <summary>Aktif şirketi çözer; companyId yoksa HQ. Host/şirketsiz scope'ta <c>null</c>
    /// (değerleme pivot=TRY identity olur). Geçersiz companyId → EntityNotFound.</summary>
    private async Task<Company?> ResolveCompanyAsync(Guid? companyId)
    {
        var cq = await _companyRepository.GetQueryableAsync(); // standart IMultiTenant → bu scope
        if (companyId.HasValue)
        {
            var c = await AsyncExecuter.FirstOrDefaultAsync(cq.Where(x => x.Id == companyId.Value));
            return c ?? throw new EntityNotFoundException(typeof(Company), companyId.Value);
        }

        return await AsyncExecuter.FirstOrDefaultAsync(cq.Where(x => x.IsHeadquarters));
    }

    private async Task<string> GetCurrencyCodeAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var u = await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync()).Where(x => x.Id == unitId));
            return u?.Code ?? string.Empty;
        }
    }

    /// <summary>Grup başına "en son" satırı (sort key desc, eşitlikte tie-break desc) seçer.</summary>
    private static Dictionary<Guid, T> LatestBy<T>(
        IEnumerable<T> rows, Func<T, Guid> keySelector, Func<T, DateTime> sortSelector, Func<T, Guid> tieBreak)
        => rows.GroupBy(keySelector)
               .ToDictionary(
                   g => g.Key,
                   g => g.OrderByDescending(sortSelector).ThenByDescending(tieBreak).First());
}
