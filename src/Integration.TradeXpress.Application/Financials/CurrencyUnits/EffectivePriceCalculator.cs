using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.Financials.ExchangeRates;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>Birim başına efektif (pivot) fiyat: efektif + gösterim bazı (Raw) + uygulanan marjlar + kur tarihi.</summary>
/// <param name="RateMissing">Birimin HİÇBİR kur bağlantısı yok (ne canlı tick, ne DB kuru, ne takip zinciri).
/// Bu durumda <paramref name="Eff"/>/<paramref name="Raw"/> 1/1 YER TUTUCUdur — kur DEĞİLDİR. Değerleme yolları
/// bu kayıtları eler; gösterim yolu sayı yerine "kur yok" yazar. Gerekçe: <see cref="EffectivePriceCalculator"/>.</param>
public sealed record EffPrice(CurrencyUnit Unit, CurrencyPrice Eff, CurrencyPrice Raw, DateTime RateDate,
    MarginSetting AppliedBuy, MarginSetting AppliedSell, bool RateMissing = false);

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
                var rateMissing = false;
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

                    // ⚠ Eksiklik ZİNCİR BOYUNCA taşınır (2026-08-05): takip edilenin kuru yoksa onun Eff'i 1/1
                    // YER TUTUCUdur; ondan türeyen değer de kur değildir. Bayrak taşınmazsa takip eden birim
                    // "kuru varmış" gibi görünür ve değerlemeye SIZAR — madenler HAS'ı takip ettiği için tam da
                    // bu yol kritik: kursuz HAS üzerinden her maden sessizce fiyatlanırdı.
                    rateMissing = parent.RateMissing;
                }
                else
                {
                    // Ne canlı tick, ne DB rate, ne takip → kur bağlantısı YOK.
                    //
                    // ⚠ 1/1 bir KUR DEĞİL, yalnız birimin listede boş geçmemesi için YER TUTUCUDUR; doğruyu
                    // söyleyen şey RateMissing bayrağıdır. Bu ayrım 2026-08-05'te KANLI canlı öğrenildi: o güne
                    // kadar 1/1 sessizce gerçek kur sayılıyordu ve HAS'ın kuru olmadığı için 7 gram has altın
                    // reçetede "7,00 TRY" olarak fiyatlanıyordu (Hakan bildirdi). Daha kötüsü, aşağı akıştaki
                    // fail-fast ağı da BAYPAS oluyordu: ProductRecipeCostCalculator "birim sözlükte yoksa
                    // MissingRate" diye tasarlanmış, ama uydurma 1/1 sayesinde TryGetValue hep BAŞARILI
                    // dönüyordu. Bu yüzden değerleme yolları (GetValuation*) artık bu kayıtları ELER —
                    // sözlükte yokluk, "kur yok"un tek ve tutarlı temsilidir.
                    //
                    // Bayrağı okumadan Eff/Raw kullanan yeni bir çağıran EKLEME; gösterim yolunda da sayı
                    // yerine "kur yok" yazılır (CurrentPriceDto.RateMissing).
                    raw = CurrencyPriceCalculator.Guard(1m, 1m);
                    rateDate = DateTime.UtcNow;
                    rateMissing = true;
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

                byId[unit.Id] = new EffPrice(unit, eff, displayBase, rateDate, appliedBuy, appliedSell, rateMissing);
                progressed = true;
            }
            pending = stillPending;
        }

        return byId.Values.ToList();
    }
}
