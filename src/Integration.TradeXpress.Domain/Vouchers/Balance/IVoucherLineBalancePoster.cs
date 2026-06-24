using System.Collections.Generic;

namespace Integration.TradeXpress.Vouchers.Balance;

/// <summary>
/// Tek bir <see cref="ProcessType"/> için "bu satır bakiyeyi nasıl etkiler" kuralını
/// taşıyan poster. Her işlem türü kendi poster'ına sahiptir; yeni tür eklerken yalnız
/// yeni poster yazmak yeterli — <see cref="VoucherBalanceCalculator"/> tüm poster'ları
/// DI ile otomatik toplar.
///
/// <para>Poster'ı OLMAYAN ProcessType'lar bakiyeyi etkilemez (sessizce atlanır).
/// Şu an yalnız <see cref="ProcessType.Cash"/> için <see cref="CashBalancePoster"/> var.</para>
/// </summary>
public interface IVoucherLineBalancePoster
{
    ProcessType ProcessType { get; }

    /// <summary>Satırın bakiye etkilerini üretir (0..N). Etki yoksa boş döner
    /// (ör. peşin nakit bakiyeye yansımaz).</summary>
    IEnumerable<BalanceEffect> Post(VoucherLine line);
}
