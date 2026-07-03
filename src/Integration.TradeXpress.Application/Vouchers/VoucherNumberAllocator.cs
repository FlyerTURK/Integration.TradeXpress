using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherNumber tahsisi: MAX(VoucherNumber for company) + 1 (lazy, ilk satırda) ve MAX+1 yarışının
/// tek noktadan yakalanması. Unique index (TenantId,CompanyId,VoucherNumber) veri bütünlüğünü zaten
/// korur; <see cref="InsertNumberedAsync"/> ihlali sert DbUpdateException yerine lokalize
/// "tekrar deneyin" mesajına çevirir (panel verisi ekranda kalır, kullanıcı yeniden kaydeder).
/// </summary>
public class VoucherNumberAllocator : ITransientDependency
{
    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public VoucherNumberAllocator(
        IRepository<Voucher, Guid> repository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _repository    = repository;
        _asyncExecuter = asyncExecuter;
    }

    public async Task<long> NextNumberAsync(Guid companyId)
    {
        var query = await _repository.GetQueryableAsync();
        var maxNumber = await _asyncExecuter.MaxAsync(
            query.Where(v => v.CompanyId == companyId).Select(v => (long?)v.VoucherNumber)) ?? 0L;
        return maxNumber + 1;
    }

    /// <summary>Numaralı fişi insert eder; VoucherNumber unique-index yarışını lokalize hataya çevirir.</summary>
    public async Task InsertNumberedAsync(Voucher voucher)
    {
        try
        {
            await _repository.InsertAsync(voucher, autoSave: true);
        }
        catch (Exception ex) when (IsVoucherNumberConflict(ex))
        {
            throw new BusinessException("TradeXpress:Voucher:NumberConflict");
        }
    }

    /// <summary>VoucherNumber unique index (TenantId,CompanyId,VoucherNumber) ihlali mi? MAX+1 yarışında
    /// (iki kullanıcı aynı anda ilk satır) ikinci insert bu ihlale düşer.</summary>
    private static bool IsVoucherNumberConflict(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e.Message.Contains("VoucherNumber", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
