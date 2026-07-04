using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Scraps;

/// <summary>
/// Scrap (Hurda) CRUD. FollowingUnit ZORUNLU; Factor 0..1. Görünürlük/guard/liste/picker davranışı
/// <see cref="FollowingUnitCatalogAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından
/// (host kataloğu + tenant kendi kayıtları; picker birim düzeni → Factor desc → Code asc).
/// </summary>
[Authorize]
public class ScrapAppService
    : FollowingUnitCatalogAppService<Scrap, ScrapGetDto, ScrapListDto, ScrapListRequestDto, ScrapCreateDto, ScrapUpdateDto>,
      IScrapAppService
{
    public ScrapAppService(
        IRepository<Scrap, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository, unitRepository)
    {
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Scraps.Create;
        UpdatePolicyName = TradeXpressPermissions.Scraps.Update;
        DeletePolicyName = TradeXpressPermissions.Scraps.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Scrap:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Scrap:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Scrap, string>> PickerOrderSelector
    {
        get { return x => x.Code; }   // kullanılmaz — picker composite sırayı tabandaki override kurar
    }

    protected override Guid FollowingUnitIdOf(Scrap entity)
    {
        return entity.FollowingUnitId;
    }

    protected override decimal CompositeFactorOf(Scrap entity)
    {
        return entity.Factor;
    }

    protected override string CodeOf(Scrap entity)
    {
        return entity.Code;
    }

    protected override Task<Scrap> MapToEntityAsync(ScrapCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Scrap(createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value, createInput.Factor, createInput.FactorChange);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task MapToEntityAsync(ScrapUpdateDto updateInput, Scrap entity)
    {
        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFactor(updateInput.Factor);
        entity.SetFactorChange(updateInput.FactorChange);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
        return Task.CompletedTask;
    }
}
