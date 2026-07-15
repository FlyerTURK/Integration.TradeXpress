using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Vouchers;

/// <summary>Fiş satırı değişim günlüğü — okuma sözleşmesi. Yazma yalnız Application seviyesinde
/// (<c>VoucherLineHistoryRecorder</c>) olur, dışarıya AÇILMAZ (kullanıcı elle log yazamaz).</summary>
public interface IVoucherLineHistoryAppService : IApplicationService
{
    /// <summary>Popup: TEK satırın tam tarihçesi — kronolojik.</summary>
    Task<List<VoucherLineHistoryDto>> GetByLineAsync(Guid voucherLineId);

    /// <summary>Log tab: karşı taraf (SubAccount/Kasa) için [start, endExclusive) aralığındaki tüm değişimler.</summary>
    Task<List<VoucherLineHistoryDto>> GetBySubAccountAsync(Guid subAccountId, DateTime start, DateTime endExclusive);
}
