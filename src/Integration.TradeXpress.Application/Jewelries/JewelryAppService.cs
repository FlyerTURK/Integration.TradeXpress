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

        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Jewelries.Create;
        UpdatePolicyName = TradeXpressPermissions.Jewelries.Update;
        DeletePolicyName = TradeXpressPermissions.Jewelries.Delete;
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

    public virtual async Task<List<JewelryListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        // Panel çalışılan şirketten farklı bir şirket verebilir → görünürlük o şirkete göre kurulur.
        // Global company filtresi working şirkete kilitli olduğundan bilinçli kapatılır;
        // görünürlüğü aşağıdaki predicate (istenen şirkete göre) zorlamaya devam eder.
        var scope = CompanyScopedQueryable.CompanyVisiblePredicate<Jewelry>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            return await GetPickerListCoreAsync(scope);
        }
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

    protected override async Task MapToEntityAsync(JewelryUpdateDto updateInput, Jewelry entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index
        // (TenantId, CompanyId, Code) ile hizalı — TenantId'yi standart filter verir.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Jewelry.Code), EntityFieldConsts.CodeMinLength, JewelryConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.Code == code,
            "TradeXpress:Jewelry:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetAttributes(updateInput.Model, updateInput.Kind, updateInput.Type, updateInput.Color, updateInput.Category, updateInput.GroupCode);
        entity.SetPricing(updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange,
                          updateInput.EntryPrice, updateInput.EntryPriceUnitId, updateInput.ExitPrice, updateInput.ExitPriceUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }
}
