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

namespace Integration.TradeXpress.Stones;

/// <summary>
/// Stone (Taş) CRUD — company-scoped. Görünür = host(TenantId null) + çalışılan şirkete-özel.
/// Sıralama: Code artan. CRUD/guard davranışı
/// <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından.
/// </summary>
[Authorize]
public class StoneAppService
    : HostCatalogCrudAppService<Stone, StoneGetDto, StoneListDto, StoneListRequestDto, StoneCreateDto, StoneUpdateDto>,
      IStoneAppService
{
    private readonly ICurrentCompany _currentCompany;

    public StoneAppService(
        IRepository<Stone, Guid> repository,
        ICurrentCompany currentCompany)
        : base(repository)
    {
        _currentCompany = currentCompany;
        LocalizationResource = typeof(TradeXpressResource);

        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Stones.Create;
        UpdatePolicyName = TradeXpressPermissions.Stones.Update;
        DeletePolicyName = TradeXpressPermissions.Stones.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Stone:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Stone:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Stone, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual async Task<List<StoneListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        // Panel çalışılan şirketten farklı bir şirket verebilir → görünürlük o şirkete göre kurulur.
        // Global company filtresi working şirkete kilitli olduğundan bilinçli kapatılır;
        // görünürlüğü aşağıdaki predicate (istenen şirkete göre) zorlamaya devam eder.
        var scope = CompanyScopedQueryable.CompanyVisiblePredicate<Stone>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            return await GetPickerListCoreAsync(scope);
        }
    }

    protected override Expression<Func<Stone, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyVisiblePredicate<Stone>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<Stone> ApplyFallbackSort(IQueryable<Stone> query, StoneListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    protected override Task<Stone> MapToEntityAsync(StoneCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Stone(
            createInput.Code, createInput.Name, createInput.CompanyId,
            createInput.IsQuantity, createInput.PriceByQuantity, createInput.PriceTypeChange,
            createInput.EntryPrice, createInput.EntryPriceUnitId, createInput.ExitPrice, createInput.ExitPriceUnitId);
        entity.SetAttributes(createInput.StoneKind, createInput.StoneType, createInput.Color, createInput.Cut,
                             createInput.Clarity, createInput.Sieve, createInput.Category, createInput.GroupCode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Stone entity)
    {
        // Update ile aynı scope/error-code — company-scoped: (TenantId, CompanyId, Code) unique index ile hizalı.
        return EnsureCodeUniqueAsync(
            entity, x => x.CompanyId == entity.CompanyId && x.Code == entity.Code,
            "TradeXpress:Stone:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(StoneUpdateDto updateInput, Stone entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index
        // (TenantId, CompanyId, Code) ile hizalı — TenantId'yi standart filter verir.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Stone.Code), EntityFieldConsts.CodeMinLength, StoneConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.Code == code,
            "TradeXpress:Stone:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetAttributes(updateInput.StoneKind, updateInput.StoneType, updateInput.Color, updateInput.Cut,
                             updateInput.Clarity, updateInput.Sieve, updateInput.Category, updateInput.GroupCode);
        entity.SetPricing(updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange,
                          updateInput.EntryPrice, updateInput.EntryPriceUnitId, updateInput.ExitPrice, updateInput.ExitPriceUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }
}
