using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.AssayOffices;

/// <summary>
/// AssayOffice (ayar evi) CRUD — <b>company-scoped</b> katalog. Kapsam DAİMA çalışılan şirket
/// (<see cref="ICurrentCompany"/>; sunucu zorlar — sızıntı önlemi, client CompanyId GÖNDERMEZ). Standart kimlik
/// (Code uppercase normalize); silme guard'ı YOK. Combo için <see cref="GetPickerListAsync"/>.
/// </summary>
[Authorize(TradeXpressPermissions.AssayOffices.Default)]
public class AssayOfficeAppService : TradeXpressAppService, IAssayOfficeAppService
{
    private readonly IRepository<AssayOffice, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "DisplayOrder", "Id" };

    public AssayOfficeAppService(IRepository<AssayOffice, Guid> repository, ICurrentCompany currentCompany)
    {
        _repository = repository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<AssayOfficeListDto>> GetListAsync(AssayOfficeListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<AssayOfficeListDto>(0, new List<AssayOfficeListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<AssayOfficeListDto>(
            totalCount, items.Select(e => ObjectMapper.Map<AssayOffice, AssayOfficeListDto>(e)).ToList());
    }

    public virtual async Task<AssayOfficeGetDto> GetAsync(Guid id)
        => ObjectMapper.Map<AssayOffice, AssayOfficeGetDto>(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.AssayOffices.Create)]
    public virtual async Task<AssayOfficeGetDto> CreateAsync(AssayOfficeCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        var e = new AssayOffice(companyId, input.Code, input.Name, displayOrder: input.DisplayOrder);
        e.SetDescription(input.Description);
        await _repository.InsertAsync(e, autoSave: true);
        return ObjectMapper.Map<AssayOffice, AssayOfficeGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.AssayOffices.Update)]
    public virtual async Task<AssayOfficeGetDto> UpdateAsync(Guid id, AssayOfficeUpdateDto input)
    {
        var e = await _repository.GetAsync(id);
        e.SetCode(input.Code);
        e.SetName(input.Name);
        e.SetDescription(input.Description);
        e.SetDisplayOrder(input.DisplayOrder);
        e.SetActive(input.IsActive);
        await _repository.UpdateAsync(e, autoSave: true);
        return ObjectMapper.Map<AssayOffice, AssayOfficeGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.AssayOffices.Delete)]
    public virtual async Task DeleteAsync(Guid id)
        => await _repository.DeleteAsync(id);

    public virtual async Task<List<AssayOfficeListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
            return new List<AssayOfficeListDto>();

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));
        return rows.Select(e => ObjectMapper.Map<AssayOffice, AssayOfficeListDto>(e)).ToList();
    }
}
