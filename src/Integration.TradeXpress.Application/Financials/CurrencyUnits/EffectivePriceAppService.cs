using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.Financials.Parities;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Efektif fiyat motoru. Ham ExchangeRate (host, pivot TRY) üstüne <b>kademe</b> uygular:
/// host marjı → (viewer tenant ise) viewer marjı (append-only CurrencyUnitMargin'den güncel=en son).
/// Cross-tenant okuma için tenant filtresi disable; görünürlük açık predicate (host null + viewer).
///
/// <para><see cref="GetCurrentPricesAsync"/> = kurlar yerel para birimine re-base'li (tek el; TR'de ÷1 = pivot).
/// <see cref="GetValuationAsync"/> = aktif şirket base'ine re-base (DEĞERLEME; parite forex yönü AYRI).</para>
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnits.Default)]
public class EffectivePriceAppService : TradeXpressAppService, IEffectivePriceAppService
{
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly IRepository<CurrencyUnitMargin, Guid> _marginRepository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Parity, Guid> _parityRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly LocalCurrencyResolver _localCurrencyResolver;
    private readonly ExchangeRateCacheService _cache;

    public EffectivePriceAppService(
        IRepository<ExchangeRate, Guid> rateRepository,
        IRepository<CurrencyUnitMargin, Guid> marginRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Company, Guid> companyRepository,
        IRepository<Parity, Guid> parityRepository,
        ICurrentCompany currentCompany,
        LocalCurrencyResolver localCurrencyResolver,
        ExchangeRateCacheService cache)
    {
        _rateRepository = rateRepository;
        _marginRepository = marginRepository;
        _unitRepository = unitRepository;
        _companyRepository = companyRepository;
        _parityRepository = parityRepository;
        _currentCompany = currentCompany;
        _localCurrencyResolver = localCurrencyResolver;
        _cache = cache;
    }

    /// <summary>
    /// Kurları çalışılan şirketin <b>YEREL para birimine</b> (ülke parası: TR→TRY, US→USD) re-base ederek döndürür —
    /// kurlar TEK ELDEN re-base'li alınır (TR dahil; host TRY-based olduğundan TR'de yerel=TRY=(1,1) → ÷1 = identity).
    /// Konvansiyon <b>her satır = "1 birim = X yerel"</b> (satır.value ÷ yerel.value, same-leg: <c>birim.Buy/yerel.Buy</c>,
    /// <c>birim.Sell/yerel.Sell</c>) — yerel satır 1.00. US şirkette TRY satırı = <c>TRYUSD</c> = 0.0211
    /// (1 TRY = 0.0211 USD), HAS = 129.6, USD = 1.00. Yön konvansiyonu YOK: tıpkı TR'de USD=47.30 ("1 USD=47.30 TRY")
    /// gibi US'te de "1 TRY = X USD" gösterilir — piyasanın evrensel okuması. Yerel para birimi marj alamaz
    /// (host TRY üzerinden gider; admin dahil TRY marjı tanımlanamaz) → yerel daima saf pivot, identity garantili.
    /// Yerel çözülemezse pivot (TRY). <b>NOT:</b> bilanço birimi (<c>BaseCurrencyUnitId</c>) DEĞİL — kur yerel paraya,
    /// pozisyon/değerleme bilançoya göredir.
    /// </summary>
    public virtual async Task<List<CurrentPriceDto>> GetCurrentPricesAsync()
    {
        var prices = await ComputeEffectiveAsync();

        var localCode = await ResolveLocalCurrencyCodeAsync();
        var local = localCode is { } lc
            ? prices.FirstOrDefault(e => string.Equals(e.Unit.Code, lc, StringComparison.OrdinalIgnoreCase))
            : null;

        // Yerel çözülemezse (host scope / feed yok) → pivot (TRY) görüntü.
        if (local is null || local.Eff is not { Buy: > 0m, Sell: > 0m })
            return OrderPrices(prices).Select(e => ToCurrentPriceDto(e, e.Eff)).ToList();

        var localId = local.Unit.Id;
        var localEff = local.Eff;

        // DİREKT re-base (parite resolver YOK): her satır "1 birim = X yerel" = birim.Buy/yerel.Buy, birim.Sell/yerel.Sell.
        // Yerel birim 1.00. US şirket: TRY = TRY.Buy/USD.Buy = 0.0211; HAS = 129.6; USD = 1.00.
        return OrderPrices(prices).Select(e =>
        {
            CurrencyPrice display = e.Unit.Id == localId
                ? CurrencyPriceCalculator.Guard(1m, 1m)
                : (e.Eff is { Buy: > 0m, Sell: > 0m } ? CurrencyPriceCalculator.ReBase(e.Eff, localEff) : e.Eff);
            return ToCurrentPriceDto(e, display);
        }).ToList();
    }

    private static IEnumerable<EffPrice> OrderPrices(IEnumerable<EffPrice> prices)
        => prices
            .OrderBy(e => e.Unit.TenantId == null ? 0 : 1)
            .ThenByDescending(e => e.Unit.AlwaysShowInBalance)
            .ThenBy(e => e.Unit.DisplayOrder)
            .ThenBy(e => e.Unit.Code);

    /// <summary>EffPrice + GÖRÜNTÜ fiyatı (<paramref name="display"/>: pivot ya da yerel-parite re-base'li) → DTO.
    /// RawBuy/RawSell DAİMA pivot kalır — marj editörü (Kur Panosu "Ayarla") feed/pivot üzerinde çalışır.</summary>
    private static CurrentPriceDto ToCurrentPriceDto(EffPrice e, CurrencyPrice display)
        => new()
        {
            Id = e.Unit.Id,
            CurrencyUnitCode = e.Unit.Code,
            CurrencyUnitName = e.Unit.Name,
            UnitType = e.Unit.Type,
            DisplayOrder = e.Unit.DisplayOrder,
            Buy = display.Buy,
            Sell = display.Sell,
            RawBuy = e.Raw.Buy,
            RawSell = e.Raw.Sell,
            MarginOnBuyType = e.AppliedBuy.Type,
            MarginOnBuyValue = e.AppliedBuy.Value,
            MarginOnSellType = e.AppliedSell.Type,
            MarginOnSellValue = e.AppliedSell.Value,
            GuardFired = display.GuardFired,
            RateDate = e.RateDate,
        };

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

        // Şirket base'ine re-base (DEĞERLEME).
        return await ReBaseToAsync(prices, company.BaseCurrencyUnitId);
    }

    /// <summary>
    /// Değerlemeyi VERİLEN base birime göre re-base eder — şube bilanço birimi şirket base'inden
    /// farklı olabildiğinden pozisyon raporu bunu kullanır. <see cref="GetValuationAsync"/>'in
    /// base-param genelleştirmesi. Boş id ya da base efektifi (feed) yoksa boş liste.
    /// </summary>
    public virtual async Task<List<ValuationPriceDto>> GetValuationByBaseAsync(Guid baseCurrencyUnitId)
    {
        if (baseCurrencyUnitId == Guid.Empty)
            return new List<ValuationPriceDto>();

        var prices = await ComputeEffectiveAsync();
        return await ReBaseToAsync(prices, baseCurrencyUnitId);
    }

    /// <summary>Efektifleri verilen base birime per-leg re-base + guard'lar; base efektifi yoksa boş.
    /// Hem şirket-base hem şube-base değerlemenin TEK ortak çekirdeği (DRY).</summary>
    private async Task<List<ValuationPriceDto>> ReBaseToAsync(List<EffPrice> prices, Guid baseUnitId)
    {
        var baseCode = await GetCurrencyCodeAsync(baseUnitId);

        // Base biriminin efektifi olmadan re-base yapılamaz (örn. feed gelmemiş USD).
        if (!prices.ToDictionary(e => e.Unit.Id).TryGetValue(baseUnitId, out var baseEff))
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

        using (DataFilter.Disable<IMultiTenant>())
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

            // Viewer (tenant) marjı COMPANY bazlı: working company'nin marjı uygulanır (branch DEĞİL).
            // Working company yoksa (host ya da tenant'ta context yoksa) viewer katmanı boş → host taban
            // gösterilir (görüntü servisi çökmez; yazma tarafı SetAsync fail-fast'tir).
            Dictionary<Guid, CurrencyUnitMargin> viewerMargin = new();
            if (viewer != null && _currentCompany.Id is { } workingCompanyId)
            {
                var viewerRows = await AsyncExecuter.ToListAsync(
                    (await _marginRepository.GetQueryableAsync())
                        .Where(m => m.TenantId == viewer && m.CompanyId == workingCompanyId));
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

    /// <summary>Çalışılan (working) şirketin YEREL para birimi kodu (TR→TRY, US→USD); yoksa null.
    /// Tek kaynak <see cref="LocalCurrencyResolver"/> (kur görüntüsü + marj guard ortak kullanır).</summary>
    private Task<string?> ResolveLocalCurrencyCodeAsync()
        => _localCurrencyResolver.ResolveCodeAsync();

    /// <summary>Görünür parite çiftlerini (host null ‖ viewer tenant) (Base,Quote) Id listesi olarak yükler —
    /// <see cref="ParityResolver"/>'ın yön/bağlantı (≤3 seviye) çözümü için.</summary>
    private async Task<List<(Guid Base, Guid Quote)>> LoadParityPairsAsync()
    {
        var viewer = CurrentTenant.Id;
        using (DataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await _parityRepository.GetQueryableAsync())
                    .Where(p => p.TenantId == null || p.TenantId == viewer)
                    .Select(p => new { p.BaseCurrencyUnitId, p.QuoteCurrencyUnitId }));
            return rows.Select(r => (r.BaseCurrencyUnitId, r.QuoteCurrencyUnitId)).ToList();
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
        using (DataFilter.Disable<IMultiTenant>())
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
