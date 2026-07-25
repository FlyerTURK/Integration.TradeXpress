using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.VariantTemplates;

/// <summary>
/// VariantTemplate (varyant tanım katalogu / demet) CRUD — <b>company-owned</b>. Kapsam DAİMA çalışılan şirket
/// (<see cref="ICurrentCompany"/>; sunucu zorlar). Standart kimlik (Code uppercase; Create+Update simetrik
/// benzersizlik) + owned özellik grupları (grup→değer; <see cref="VariantTemplate.SetAttributes"/> boş grup/değer eler).
/// "Katalogtan Uygula" için <see cref="GetPickerListAsync"/> (liste) + <see cref="GetAsync"/> (tam graf).
/// </summary>
[Authorize(TradeXpressPermissions.VariantTemplates.Default)]
public class VariantTemplateAppService : TradeXpressAppService, IVariantTemplateAppService
{
    private readonly IRepository<VariantTemplate, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "DisplayOrder", "Id" };

    public VariantTemplateAppService(IRepository<VariantTemplate, Guid> repository, ICurrentCompany currentCompany)
    {
        _repository = repository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<VariantTemplateListDto>> GetListAsync(VariantTemplateListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<VariantTemplateListDto>(0, new List<VariantTemplateListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        return new PagedResultDto<VariantTemplateListDto>(
            totalCount, items.Select(e => ObjectMapper.Map<VariantTemplate, VariantTemplateListDto>(e)).ToList());
    }

    public virtual async Task<VariantTemplateGetDto> GetAsync(Guid id)
        => ObjectMapper.Map<VariantTemplate, VariantTemplateGetDto>(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.VariantTemplates.Create)]
    public virtual async Task<VariantTemplateGetDto> CreateAsync(VariantTemplateCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(VariantTemplate.Code), EntityFieldConsts.CodeMinLength, VariantTemplateConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var e = new VariantTemplate(companyId, input.Code, input.Name, input.DisplayOrder);
        e.SetDescription(input.Description);
        e.SetAttributes(MapToEntityAttributes(input.Attributes));
        await _repository.InsertAsync(e, autoSave: true);
        return ObjectMapper.Map<VariantTemplate, VariantTemplateGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.VariantTemplates.Update)]
    public virtual async Task<VariantTemplateGetDto> UpdateAsync(Guid id, VariantTemplateUpdateDto input)
    {
        var e = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(e, input.Code);
        e.SetName(input.Name);
        e.SetDescription(input.Description);
        e.SetDisplayOrder(input.DisplayOrder);
        e.SetActive(input.IsActive);
        e.SetAttributes(MapToEntityAttributes(input.Attributes));
        await _repository.UpdateAsync(e, autoSave: true);
        return ObjectMapper.Map<VariantTemplate, VariantTemplateGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.VariantTemplates.Delete)]
    public virtual async Task DeleteAsync(Guid id)
        => await _repository.DeleteAsync(id);

    public virtual async Task<List<VariantTemplateListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
            return new List<VariantTemplateListDto>();

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));
        return rows.Select(e => ObjectMapper.Map<VariantTemplate, VariantTemplateListDto>(e)).ToList();
    }

    private async Task ApplyCodeChangeAsync(VariantTemplate entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, VariantTemplateConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return;
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(a => a.CompanyId == companyId && a.Id != excludeId && a.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:VariantTemplate:CodeAlreadyExists");
        }
    }

    // DTO grafını entity owned nesnelerine çevirir (entity SetAttributes boş grup/değer eler + trim + sıralar).
    private static IEnumerable<VariantTemplateAttribute> MapToEntityAttributes(List<VariantTemplateAttributeDto>? attributes)
    {
        return (attributes ?? new List<VariantTemplateAttributeDto>())
            .Select(a => new VariantTemplateAttribute(
                a.Name,
                a.DisplayOrder,
                (a.Values ?? new List<VariantTemplateAttributeValueDto>())
                    .Select(v => new VariantTemplateAttributeValue(v.Value, v.DisplayOrder))
                    .ToList()));
    }
}
