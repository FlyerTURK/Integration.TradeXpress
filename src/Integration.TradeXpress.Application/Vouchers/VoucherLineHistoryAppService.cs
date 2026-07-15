using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş satırı değişim günlüğü — okuma yüzü. Yetki <see cref="VoucherAppService"/>'in okuma metotlarıyla
/// AYNI kapıdır ([Authorize] sınıf düzeyinde; ek per-tip guard yok — tarihçe salt-okunur görüntüdür).
/// </summary>
[Authorize]
public class VoucherLineHistoryAppService : TradeXpressAppService, IVoucherLineHistoryAppService
{
    private readonly IRepository<VoucherLineHistory, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    public VoucherLineHistoryAppService(
        IRepository<VoucherLineHistory, Guid> repository,
        ICurrentCompany currentCompany)
    {
        _repository     = repository;
        _currentCompany = currentCompany;
    }

    public async Task<List<VoucherLineHistoryDto>> GetByLineAsync(Guid voucherLineId)
    {
        var companyId = EnsureCurrentCompanyId();
        var entities = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(h => h.CompanyId == companyId && h.VoucherLineId == voucherLineId)
                .OrderBy(h => h.CreationTime));

        return await ToDtosAsync(entities);
    }

    public async Task<List<VoucherLineHistoryDto>> GetBySubAccountAsync(Guid subAccountId, DateTime start, DateTime endExclusive)
    {
        var companyId = EnsureCurrentCompanyId();
        var entities = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(h => h.CompanyId == companyId
                         && h.SubAccountId == subAccountId
                         && h.CreationTime >= start
                         && h.CreationTime < endExclusive)
                .OrderByDescending(h => h.CreationTime));

        return await ToDtosAsync(entities);
    }

    /// <summary>Sızıntı önleme — VoucherAppService.Guards ile AYNI ilke: CompanyId working-context'ten zorlanır.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        return _currentCompany.Id
               ?? throw new Volo.Abp.BusinessException("TradeXpress:VoucherLineHistory:CompanyContextRequired");
    }

    private async Task<List<VoucherLineHistoryDto>> ToDtosAsync(List<VoucherLineHistory> entities)
    {
        var dtos = entities.Select(h => new VoucherLineHistoryDto
        {
            Id            = h.Id,
            VoucherLineId = h.VoucherLineId,
            VoucherId     = h.VoucherId,
            ChangeType    = h.ChangeType,
            VoucherNumber = h.VoucherNumber,
            VoucherDate   = h.VoucherDate,
            ProcessType   = h.ProcessType,
            ProcessCode   = h.ProcessCode,
            CommodityCode = h.CommodityCode,
            Quantity      = h.Quantity,
            Amount        = h.Amount,
            Total         = h.Total,
            MainUnitCode  = h.MainUnitCode,
            Description   = h.Description,
            SubAccountId  = h.SubAccountId,
            CreatorId     = h.CreatorId,
            CreationTime  = h.CreationTime,
            Snapshot      = VoucherLineHistorySerializer.Deserialize(h.SnapshotJson),
        }).ToList();

        // VoucherCodeResolver.ResolveCreatorNamesAsync VoucherLineDto listesi bekler (History DTO'su değil) →
        // burada doğrudan çözülür (aynı desen: CreatorId → IdentityUser.UserName).
        var creatorIds = dtos.Where(d => d.CreatorId.HasValue).Select(d => d.CreatorId!.Value).Distinct().ToList();
        if (creatorIds.Count > 0)
        {
            var users = await AsyncExecuter.ToListAsync(
                (await LazyServiceProvider.LazyGetRequiredService<IRepository<Volo.Abp.Identity.IdentityUser, Guid>>().GetQueryableAsync())
                    .Where(u => creatorIds.Contains(u.Id)));
            var userDict = users.ToDictionary(u => u.Id, u => u.UserName);
            foreach (var d in dtos)
            {
                if (d.CreatorId.HasValue && userDict.TryGetValue(d.CreatorId.Value, out var name))
                {
                    d.CreatorName = name;
                }
            }
        }

        return dtos;
    }
}
