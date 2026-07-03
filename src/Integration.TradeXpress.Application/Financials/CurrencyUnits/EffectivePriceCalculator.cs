using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Financials.ExchangeRates;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>Birim başına efektif (pivot) fiyat: efektif + gösterim bazı (Raw) + uygulanan marjlar + kur tarihi.</summary>
public sealed record EffPrice(CurrencyUnit Unit, CurrencyPrice Eff, CurrencyPrice Raw, DateTime RateDate,
    MarginSetting AppliedBuy, MarginSetting AppliedSell);

/// <summary>
/// Efektif fiyat motorunun SAF hesap çekirdeği (veri erişimi yok — sorgular/cache okuma
/// <see cref="EffectivePriceAppService"/>'te kalır): birim başına ham fiyat seçimi (canlı tick → DB snapshot →
/// takip türevi → varsayılan 1/1), marj kademesi (host → viewer) ve veri-izolasyonlu gösterim bazı.
/// </summary>
public class EffectivePriceCalculator : ITransientDependency
{
    /// <summary>
    /// Bağımlılık-sıralı çözüm: doğrudan (feed/DB) fiyatı olan birimler hemen; takip eden birimler
    /// takip edilenin EFEKTİFİ hesaplandıktan SONRA türetilir. Tur tur ilerle (zincir A→B→C);
    /// ilerleme durunca kalanlar çözülemez (fiyatsız ya da döngü) → atlanır. Cycle-safe.
    /// </summary>
    /// <param name="units">Görünür birimler (host + viewer tenant).</param>
    /// <param name="latestRaw">Birim → en son ham DB kuru (as-of cutoff uygulanmış).</param>
    /// <param name="hostMargin">Birim → en son host marjı.</param>
    /// <param name="viewerMargin">Birim → en son viewer (tenant, working company) marjı; host/context yoksa boş.</param>
    /// <param name="liveQuotes">Canlı tick cache'i (birim koduna göre; worker her poll'da günceller).</param>
    /// <param name="useLiveQuotes">Canlı tick kullanılsın mı (as-of/historical değerlemede ATLANIR).</param>
    /// <param name="viewerIsTenant">Bakan taraf tenant mı (veri izolasyonu: tenant host'un HAM fiyatını görmez).</param>
    public List<EffPrice> Resolve(
        IReadOnlyList<CurrencyUnit> units,
        IReadOnlyDictionary<Guid, ExchangeRate> latestRaw,
        IReadOnlyDictionary<Guid, CurrencyUnitMargin> hostMargin,
        IReadOnlyDictionary<Guid, CurrencyUnitMargin> viewerMargin,
        IReadOnlyDictionary<string, MarketQuote> liveQuotes,
        bool useLiveQuotes,
        bool viewerIsTenant)
    {
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
                if (useLiveQuotes && liveQuotes.TryGetValue(unit.Code, out var q) && q.Buy > 0m && q.Sell > 0m)
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
                    (viewerIsTenant && viewerMargin.TryGetValue(unit.Id, out var vm)) ? (vm.MarginOnBuy, vm.MarginOnSell) : null;

                var layers = new List<(MarginSetting, MarginSetting)>();
                if (hostLayer is { } hl) layers.Add(hl);
                if (viewerLayer is { } vl) layers.Add(vl);
                var eff = CurrencyPriceCalculator.Cascade(raw, layers.ToArray());

                // VERİ İZOLASYONU (kaynakta): TENANT, host'un HAM fiyatını ASLA görmez. Gösterilen "baz" =
                // host EFEKTİFİ (raw + host marjı); gösterilen marj = viewer'ın KENDİ marjı (tenant marjı).
                // HOST için baz = ham, marj = host'un kendi marjı. Böylece her zaman: baz · marj = nihai.
                CurrencyPrice displayBase;
                MarginSetting appliedBuy, appliedSell;
                if (!viewerIsTenant)
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
