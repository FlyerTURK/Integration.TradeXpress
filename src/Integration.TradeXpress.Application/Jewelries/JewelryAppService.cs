using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Jewelries;

/// <summary>
/// Jewelry (Mücevher) CRUD — company-scoped. Görünür = host(TenantId null) + çalışılan şirkete-özel
/// (CompanyId == çalışılan; CompanyId null = holding-host). Sıralama: Code artan. CRUD/guard davranışı
/// <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından.
/// </summary>
[Authorize]
public class JewelryAppService
    : HostCatalogCrudAppService<Jewelry, JewelryGetDto, JewelryListDto, JewelryListRequestDto, JewelryCreateDto, JewelryUpdateDto>,
      IJewelryAppService
{
    private readonly ICurrentCompany _currentCompany;

    public JewelryAppService(
        IRepository<Jewelry, Guid> repository,
        ICurrentCompany currentCompany)
        : base(repository)
    {
        _currentCompany = currentCompany;
        LocalizationResource = typeof(TradeXpressResource);
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Jewelry:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Jewelry:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Jewelry, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual Task<List<JewelryListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        // Panel çalışılan şirketten farklı bir şirket verebilir → görünürlük o şirkete göre kurulur.
        var scope = CompanyScopedQueryable.CompanyVisiblePredicate<Jewelry>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);
        return GetPickerListCoreAsync(scope);
    }

    protected override Expression<Func<Jewelry, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyVisiblePredicate<Jewelry>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<Jewelry> ApplyFallbackSort(IQueryable<Jewelry> query, JewelryListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    protected override Task<Jewelry> MapToEntityAsync(JewelryCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Jewelry(
            createInput.Code, createInput.Name, createInput.CompanyId,
            createInput.IsQuantity, createInput.PriceByQuantity, createInput.PriceTypeChange,
            createInput.EntryPrice, createInput.EntryPriceUnitId, createInput.ExitPrice, createInput.ExitPriceUnitId);
        entity.SetAttributes(createInput.Model, createInput.Kind, createInput.Type, createInput.Color, createInput.Category, createInput.GroupCode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task MapToEntityAsync(JewelryUpdateDto updateInput, Jewelry entity)
    {
        entity.SetName(updateInput.Name);
        entity.SetAttributes(updateInput.Model, updateInput.Kind, updateInput.Type, updateInput.Color, updateInput.Category, updateInput.GroupCode);
        entity.SetPricing(updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange,
                          updateInput.EntryPrice, updateInput.EntryPriceUnitId, updateInput.ExitPrice, updateInput.ExitPriceUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
        return Task.CompletedTask;
    }
}
