using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Reports;

/// <summary>Tek bacak — rezervasyon mu, ne kadar. Rapor entity'lerinden bağımsızdır ki hesap saf kalsın.</summary>
public readonly record struct ReservationLeg(bool IsReservation, decimal Amount, decimal Quantity);

/// <summary>Bir stok satırının fiziksel + taahhüt toplamları.</summary>
public readonly record struct ReservationTotals(
    decimal InAmount,
    decimal OutAmount,
    decimal NetAmount,
    decimal InQuantity,
    decimal OutQuantity,
    decimal NetQuantity,
    decimal ReservedInAmount,
    decimal ReservedOutAmount,
    decimal ReservedInQuantity,
    decimal ReservedOutQuantity)
{
    /// <summary>Satılabilir ağırlık = Net − müşteriye ayrılan.
    /// <para><b>ReservedIn EKLENMEZ</b>: o "tedarikçiden beklenen"dir, elimizde DEĞİLDİR. Eklemek,
    /// gelmemiş malı satılabilir göstermek olurdu.</para></summary>
    public decimal AvailableAmount
    {
        get { return NetAmount - ReservedOutAmount; }
    }

    /// <summary>Satılabilir adet = Net − müşteriye ayrılan (aynı gerekçe).</summary>
    public decimal AvailableQuantity
    {
        get { return NetQuantity - ReservedOutQuantity; }
    }
}

/// <summary>
/// REZERVASYON AYRIŞTIRMASI — bir stok satırının bacaklarını "fiziksel" ve "taahhüt" diye ikiye ayıran
/// saf hesap.
///
/// <para><b>Neden ortak sınıf, ortak ARAYÜZ değil:</b> Metal <c>(emtia, varyant, BİRİM)</c> ile gruplar ve
/// asıl ölçüsü gramdır; Good <c>(emtia, varyant)</c> ile gruplar ve asıl ölçüsü adettir. Ortak bir rapor
/// arayüzü en küçük ortak paydayı uydurur, iki çağıran da geri cast eder — anlam bağı (connascence of
/// meaning) doğar. Gerçekten ortak olan şey gruplama değil, <b>aritmetiğin kendisidir</b>: bu sınıf yalnız
/// onu paylaştırır.</para>
///
/// <para><b>Neden kritik:</b> rezervasyon fiziksel <c>Net</c>'e GİRMEZ. Girerse iki hata birden doğar —
/// stok olmayan mal varmış görünür ve yürüyen bakiye şişer. Bu ayrım tek satırlık bir filtreyle bozulabilir,
/// bu yüzden tek yerde yaşar ve doğrudan test edilir.</para>
/// </summary>
public static class ReservationSplit
{
    public static ReservationTotals Compute(IEnumerable<ReservationLeg> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var totals = new ReservationTotals();

        decimal inAmount = 0m, outAmount = 0m, netAmount = 0m;
        decimal inQuantity = 0m, outQuantity = 0m, netQuantity = 0m;
        decimal reservedInAmount = 0m, reservedOutAmount = 0m;
        decimal reservedInQuantity = 0m, reservedOutQuantity = 0m;

        foreach (var leg in legs)
        {
            if (leg.IsReservation)
            {
                // Taahhüt: işaret YÖNÜ belirler — çıkış (−) müşteriye ayrılan, giriş (+) tedarikçiden beklenen.
                if (leg.Amount < 0m)
                {
                    reservedOutAmount += -leg.Amount;
                }
                else
                {
                    reservedInAmount += leg.Amount;
                }

                if (leg.Quantity < 0m)
                {
                    reservedOutQuantity += -leg.Quantity;
                }
                else
                {
                    reservedInQuantity += leg.Quantity;
                }

                continue;   // fiziksel toplamlara KATILMAZ — bu sınıfın var oluş sebebi
            }

            if (leg.Amount > 0m)
            {
                inAmount += leg.Amount;
            }
            else
            {
                outAmount += -leg.Amount;
            }

            netAmount += leg.Amount;

            if (leg.Quantity > 0m)
            {
                inQuantity += leg.Quantity;
            }
            else
            {
                outQuantity += -leg.Quantity;
            }

            netQuantity += leg.Quantity;
        }

        return totals with
        {
            InAmount = inAmount,
            OutAmount = outAmount,
            NetAmount = netAmount,
            InQuantity = inQuantity,
            OutQuantity = outQuantity,
            NetQuantity = netQuantity,
            ReservedInAmount = reservedInAmount,
            ReservedOutAmount = reservedOutAmount,
            ReservedInQuantity = reservedInQuantity,
            ReservedOutQuantity = reservedOutQuantity,
        };
    }
}
