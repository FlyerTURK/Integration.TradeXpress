using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Application;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.SpecialCodes;

/// <summary>
/// Özel Kod (SpecialCode) CRUD — company-scoped, (EntityName, PropertyName) bağlamına kapsamlı. Görünürlük Good/
/// Jewelry ile aynı (host + holding-host + çalışılan şirket). Ek olarak: bağlam-filtreli picker beslemesi
/// (<see cref="GetForContextAsync"/>) + parent'ın aynı bağlamda olması & döngü olmaması guard'ı.
/// </summary>
[Authorize]
public class SpecialCodeAppService
    : HostCatalogCrudAppService<SpecialCode, SpecialCodeGetDto, SpecialCodeListDto, SpecialCodeListRequestDto, SpecialCodeCreateDto, SpecialCodeUpdateDto>,
      ISpecialCodeAppService
{
    private readonly IRepository<SpecialCode, Guid> _repository;
    private readonly ICurrentCompany _currentCompany;

    public SpecialCodeAppService(
        IRepository<SpecialCode, Guid> repository,
        ICurrentCompany currentCompany)
        : base(repository)
    {
        _repository = repository;
        _currentCompany = currentCompany;
        LocalizationResource = typeof(TradeXpressResource);

        CreatePolicyName = TradeXpressPermissions.SpecialCodes.Create;
        UpdatePolicyName = TradeXpressPermissions.SpecialCodes.Update;
        DeletePolicyName = TradeXpressPermissions.SpecialCodes.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id", "EntityName", "PropertyName" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:SpecialCode:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:SpecialCode:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<SpecialCode, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual async Task<List<SpecialCodeListDto>> GetForContextAsync(
        string entityName, string propertyName, Guid? companyId = null)
    {
        var en = (entityName ?? string.Empty).Trim();
        var pn = (propertyName ?? string.Empty).Trim();

        // Görünürlük merkezî predicate ile (host + holding-host + verilen/çalışılan şirket) — ABP filtreleri
        // bilinçli kapatılır, tek otorite predicate. Yalnız AKTİF kodlar (picker combo'su).
        using (DataFilter.Disable<ICompanyScoped>())
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = (await _repository.GetQueryableAsync())
                .WhereCompanyVisible(CurrentTenant.Id, companyId ?? _currentCompany.Id)
                .Where(x => x.EntityName == en && x.PropertyName == pn && x.IsActive)
                .OrderBy(x => x.Code);

            var entities = await AsyncExecuter.ToListAsync(query);
            return entities.Select(MapListWithIsGlobal).ToList();
        }
    }

    protected override Expression<Func<SpecialCode, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyVisiblePredicate<SpecialCode>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<SpecialCode> ApplyFallbackSort(IQueryable<SpecialCode> query, SpecialCodeListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    protected override async Task<SpecialCode> MapToEntityAsync(SpecialCodeCreateDto createInput)
    {
        var entity = new SpecialCode(
            createInput.EntityName, createInput.PropertyName, createInput.Code, createInput.Name,
            createInput.CompanyId, createInput.ParentId);
        entity.SetDescription(createInput.Description);
        await EnsureValidParentAsync(entity);
        return entity;
    }

    protected override Task EnsureCreateCodeUniqueAsync(SpecialCode entity)
    {
        return EnsureCodeUniqueAsync(
            entity,
            x => x.CompanyId == entity.CompanyId && x.EntityName == entity.EntityName
              && x.PropertyName == entity.PropertyName && x.Code == entity.Code,
            "TradeXpress:SpecialCode:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(SpecialCodeUpdateDto updateInput, SpecialCode entity)
    {
        // Kod düzenlenebilir; benzersizlik scope'u (CompanyId + bağlam + Code). Bağlam set-once → değişmez.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(SpecialCode.Code), EntityFieldConsts.CodeMinLength, SpecialCodeConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.EntityName == entity.EntityName
                      && x.PropertyName == entity.PropertyName && x.Code == code,
            "TradeXpress:SpecialCode:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetParent(updateInput.ParentId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
        await EnsureValidParentAsync(entity);
    }

    /// <summary>Parent guard: null=kök (serbest). Parent VAR olmalı + AYNI bağlamda (CompanyId+EntityName+PropertyName)
    /// olmalı; parent zinciri kendine dönmemeli (döngü). ABP filtreleri kapalı — bağlam kontrolü tek otorite.</summary>
    private async Task EnsureValidParentAsync(SpecialCode entity)
    {
        if (entity.ParentId is not { } parentId)
        {
            return;
        }

        using (DataFilter.Disable<ICompanyScoped>())
        using (DataFilter.Disable<IMultiTenant>())
        {
            var parent = await _repository.FindAsync(parentId);
            if (parent is null)
            {
                throw new BusinessException("TradeXpress:SpecialCode:ParentNotFound");
            }

            if (parent.CompanyId != entity.CompanyId || parent.EntityName != entity.EntityName
                || parent.PropertyName != entity.PropertyName)
            {
                throw new BusinessException("TradeXpress:SpecialCode:ParentContextMismatch");
            }

            // Döngü: parent zincirini yukarı yürü; entity'nin Id'sine (update'te dolu) ulaşırsa döngüdür.
            // Create'te entity.Id boş → zincir ona ulaşamaz (döngü imkânsız); yalnız bağlam kontrolü geçerli.
            var current = parent;
            var guard = 0;
            while (current is not null)
            {
                if (current.Id == entity.Id)
                {
                    throw new BusinessException("TradeXpress:SpecialCode:ParentCycle");
                }

                if (current.ParentId is not { } nextId || ++guard > 64)
                {
                    break;
                }

                current = await _repository.FindAsync(nextId);
            }
        }
    }
}
