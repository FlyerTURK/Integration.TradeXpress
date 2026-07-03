using System;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Saf (infra'sız) işlem-satırı hesap/karar motoru — UI-agnostik <b>business rule</b>
/// (ERPPROV3 <c>VoucherLineCalculator</c> paritesi). Aynı motoru Blazor (in-process)
/// ve sunucu (kayıt öncesi yetkili pas) çağırır → tek kaynak, kod tekrarı yok, UI'da
/// karar yok. Dış bağımlılıklar delege ile gelir:
/// <list type="bullet">
///   <item><paramref name="buyRateOf"/>: birim Id → TL <b>alış</b> kuru (TRY→1; bilinmiyorsa 0).</item>
///   <item><paramref name="parityMainOf"/>: iki birim → parite kaydının <b>Main (base)</b>
///   birimi (kanonik yön), kayıt yoksa null.</item>
/// </list>
///
/// <para><b>Parite yönü:</b> görünen PayFactor = <c>buy(base)/buy(quote)</c> (örn.
/// USD/TRY = 45.59), çağrı sırasından bağımsız. Ana birim kaydın Main'iyse <c>çarp</c>
/// → PayTotal = Amount × PayFactor; değilse <c>böl</c> → PayTotal = Amount / PayFactor.</para>
/// </summary>
public static class VoucherLineCalculator
{
    public static VoucherLineCalcResult Calculate(
        VoucherLineCalcInput i,
        Func<Guid, decimal> buyRateOf,
        Func<Guid, Guid, Guid?> parityMainOf)
    {
        ArgumentNullException.ThrowIfNull(buyRateOf);
        ArgumentNullException.ThrowIfNull(parityMainOf);

        var isInflow  = ((int)i.Direction % 2) == 0;
        var paySource = i.PaymentType == ProcessPaymentType.WithCash
            ? PayCommoditySource.CashInstruments
            : PayCommoditySource.Units;

        return i.ProcessType switch
        {
            ProcessType.Cash    => CalculateCash(i, buyRateOf, parityMainOf, isInflow, paySource),

            // Çevir / Vadeli / Hurda / Maden: peşin/bedelli pay-bacağı Nakit ile AYNI parite
            // matematiğidir (pay = Unit). Diğer ödeme tiplerinde panel kendi hesaplar; motor
            // çıktısını yoksayar.
            ProcessType.Convert => CalculateCash(i, buyRateOf, parityMainOf, isInflow, PayCommoditySource.Units),
            ProcessType.Future  => CalculateCash(i, buyRateOf, parityMainOf, isInflow, PayCommoditySource.Units),
            ProcessType.Scrap   => CalculateCash(i, buyRateOf, parityMainOf, isInflow, PayCommoditySource.Units),
            ProcessType.Metal   => CalculateCash(i, buyRateOf, parityMainOf, isInflow, PayCommoditySource.Units),

            // Takoz / Çeşni: parite pay-bacağı YOK — tüm bacak matematiği ayrı (çok-metalli)
            // hesaplayıcıda olacak. Bu motor bilinçli devre dışı → passthrough.
            ProcessType.Bullion => Passthrough(i, isInflow, PayCommoditySource.Units),
            ProcessType.Assay   => Passthrough(i, isInflow, PayCommoditySource.Units),

            _ => Passthrough(i, isInflow, paySource),
        };
    }

    // ── NAKİT (Cash) ────────────────────────────────────────────────────────────
    private static VoucherLineCalcResult CalculateCash(
        VoucherLineCalcInput i, Func<Guid, decimal> buyRateOf, Func<Guid, Guid, Guid?> parityMainOf,
        bool isInflow, PayCommoditySource paySource)
    {
        var sameUnit = i.MainUnitId is { } mEq && i.PayUnitId is { } pEq && mEq == pEq;

        // ── Parite yönü + kanonik (doğal) piyasa değeri ──
        decimal marketPrice;   // görünen doğal parite (örn. 45.59), yön ne olursa olsun
        bool    carp;          // true → ×, false → ÷
        if (sameUnit)
        {
            marketPrice = 1m;
            carp        = true;
        }
        else if (i.MainUnitId is { } mu && i.PayUnitId is { } pu)
        {
            var baseUnit = parityMainOf(mu, pu);
            if (baseUnit is { } b)
            {
                var quote    = (b == mu) ? pu : mu;
                var buyQuote = buyRateOf(quote);
                marketPrice  = buyQuote != 0m ? buyRateOf(b) / buyQuote : 0m;   // buy(base)/buy(quote) → doğal
                carp         = (mu == b);                                       // ana birim kaydın Main'iyse çarp
            }
            else
            {
                // Parite kaydı YOK → kırılma yok: ana ve karşı birimin son alış fiyatlarıyla
                // DÜZ parite varsay (PayFactor = buy(Main)/buy(Pay), çarpım yönünde).
                var buyPay = buyRateOf(pu);
                marketPrice = buyPay != 0m ? buyRateOf(mu) / buyPay : 0m;
                carp        = true;
            }
        }
        else
        {
            marketPrice = 0m;
            carp        = true;
        }

        // ── Amount / PayFactor / PayTotal (Çarp/Böl) ──
        decimal amount = i.Amount;   // Tutar (PayTotal) düzenlenince ve Miktar boşken türetilir
        decimal payFactor;
        decimal payTotal;
        if (sameUnit)
        {
            payFactor = 1m;
            payTotal  = amount;
        }
        else if (i.EditedField == EditedField.PayTotal)
        {
            payTotal = i.PayTotal;
            if (amount != 0m)
            {
                // Miktar biliniyor → Fiyat geri-hesap (doğal yönde).
                payFactor = carp
                    ? payTotal / amount
                    : (payTotal != 0m ? amount / payTotal : marketPrice);
            }
            else
            {
                // Miktar boş → mevcut/market Fiyat ile MİKTAR'ı türet.
                payFactor = i.PayFactor != 0m ? i.PayFactor : marketPrice;
                amount    = payFactor != 0m ? (carp ? payTotal / payFactor : payTotal * payFactor) : 0m;
            }
        }
        else if (i.EditedField is EditedField.Commodity or EditedField.PayUnit
                                or EditedField.PaymentType or EditedField.None)
        {
            // Yapısal değişim → pariteyi yükle (doğal değer).
            payFactor = marketPrice != 0m ? marketPrice : i.PayFactor;
            payTotal  = carp ? amount * payFactor : (payFactor != 0m ? amount / payFactor : 0m);
        }
        else
        {
            // Amount / PayFactor / Direction → mevcut Fiyat'ı koru, Tutar'ı hesapla.
            payFactor = i.PayFactor != 0m ? i.PayFactor : marketPrice;
            payTotal  = carp ? amount * payFactor : (payFactor != 0m ? amount / payFactor : 0m);
        }

        // ── Kâr = Satış − Maliyet (alış kurlarıyla, TL; yönden bağımsız) ──
        var buyMain = i.MainUnitId is { } pm ? buyRateOf(pm) : 0m;
        var buyPayU = i.PayUnitId  is { } pp ? buyRateOf(pp) : 0m;
        var profit = (buyMain != 0m && buyPayU != 0m)
            ? (payTotal * buyPayU) - (amount * buyMain)
            : 0m;

        return new VoucherLineCalcResult(
            Amount:             amount,
            Factor:             1m,
            Total:              amount,
            PayFactor:          payFactor,
            MarketPrice:        marketPrice,
            PayTotal:           payTotal,
            Profit:             profit,
            PayFactorReadOnly:  sameUnit,
            PayTotalReadOnly:   sameUnit,
            PayCommoditySource: paySource,
            IsInflow:           isInflow);
    }

    private static VoucherLineCalcResult Passthrough(
        VoucherLineCalcInput i, bool isInflow, PayCommoditySource paySource)
    {
        return new(
            Amount:             i.Amount,
            Factor:             i.Factor,
            Total:              i.Total,
            PayFactor:          i.PayFactor,
            MarketPrice:        i.MarketPrice,
            PayTotal:           i.PayTotal,
            Profit:             0m,
            PayFactorReadOnly:  false,
            PayTotalReadOnly:   false,
            PayCommoditySource: paySource,
            IsInflow:           isInflow);
    }
}
