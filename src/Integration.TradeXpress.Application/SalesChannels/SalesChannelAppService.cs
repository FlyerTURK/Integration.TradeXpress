using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Satış kanalı BİRLEŞİK (polymorphic) app service'i — TÜM TPT alt-tiplerini tek listede sunar + tür-bağımsız silme.
/// Base repository üzerinden sorgular (EF concrete alt-tipi materyalize eder); <see cref="SalesChannelListDto.ChannelType"/>
/// concrete tipten türetilir. Company-owned (sunucu <see cref="ICurrentCompany"/> zorlar). Tipe-özel oluşturma/güncelleme
/// <see cref="ISalesChannelTrN11AppService"/> / <see cref="ISalesChannelTrTrendyolAppService"/>'te.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelAppService : TradeXpressAppService, ISalesChannelAppService
{
    private readonly IRepository<SalesChannelBase, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public SalesChannelAppService(
        IRepository<SalesChannelBase, Guid> repository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<SalesChannelListDto>> GetListAsync(SalesChannelListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<SalesChannelListDto>(0, new List<SalesChannelListDto>());
        }

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        // ObjectMapper (base property'leri) + türetilmiş ChannelType (concrete alt-tipten) — inline projeksiyon.
        return new PagedResultDto<SalesChannelListDto>(
            totalCount,
            items.Select(e =>
            {
                var dto = ObjectMapper.Map<SalesChannelBase, SalesChannelListDto>(e);
                dto.ChannelType = e switch
                {
                    SalesChannelTrTrendyol => SalesChannelType.TrTrendyol,
                    SalesChannelEtsy => SalesChannelType.Etsy,
                    _ => SalesChannelType.TrN11,
                };
                return dto;
            }).ToList());
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı İKİ KATLI: global company query-filter + AÇIK CompanyId koşulu (GetOwnedAsync).
        // Yalnız filtreye güvenmek yetmiyordu — şirket bağlamı kurulmamış bir çağrıda (ör. HTTP API) filtre
        // permissive kola düşüyor ve koruma sessizce yok oluyordu. TPT cascade alt-tipi düşürür.
        var entity = await _repository.GetOwnedAsync(_currentCompany, id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    public virtual async Task<List<SalesChannelType>> GetExistingChannelTypesAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<SalesChannelType>();
        }

        // TPT: OfType<> alt-tip tablosuna göre süzer. IsActive'e BAKILMAZ — pasif de olsa "tür var" (tekillik kuralı).
        var baseQuery = (await _repository.GetQueryableAsync()).Where(x => x.CompanyId == companyId);

        var existing = new List<SalesChannelType>();
        if (await AsyncExecuter.AnyAsync(baseQuery.OfType<SalesChannelTrN11>()))
        {
            existing.Add(SalesChannelType.TrN11);
        }

        if (await AsyncExecuter.AnyAsync(baseQuery.OfType<SalesChannelTrTrendyol>()))
        {
            existing.Add(SalesChannelType.TrTrendyol);
        }

        if (await AsyncExecuter.AnyAsync(baseQuery.OfType<SalesChannelEtsy>()))
        {
            existing.Add(SalesChannelType.Etsy);
        }

        return existing;
    }
}
