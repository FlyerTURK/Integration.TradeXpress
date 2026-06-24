using System;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Bir <see cref="VoucherLine"/>'ın TEK bir para birimi (CurrencyUnit) bakiyesine
/// etkisi. Bir satır 0..N etki üretebilir (iki bacaklı işlemler birden çok birimi
/// etkileyebilir).
///
/// <para><b>İşaret konvansiyonu:</b> <see cref="Amount"/> &gt; 0 = ALACAK (hesap
/// lehine), &lt; 0 = BORÇ. Net bakiye = aynı birimdeki etkilerin toplamı.</para>
/// </summary>
public readonly record struct BalanceEffect(Guid UnitId, decimal Amount);
