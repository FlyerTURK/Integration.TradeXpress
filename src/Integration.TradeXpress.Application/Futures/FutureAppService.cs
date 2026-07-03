using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Futures;

/// <summary>
/// Future (Vadeli) CRUD. FollowingUnit ZORUNLU; FollowingFactor &gt;0. Görünürlük/guard/liste/picker davranışı
/// <see cref="FollowingUnitCatalogAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından
/// (host kataloğu + tenant kendi kayıtları; picker birim düzeni → FollowingFactor desc → Code asc).
/// </summary>
[Authorize]
public class FutureAppService
    : FollowingUnitCatalogAppService<Future, FutureGetDto, FutureListDto, FutureListRequestDto, FutureCreateDto, FutureUpdateDto>,
      IFutureAppService
{
    public FutureAppService(
        IRepository<Future, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository, unitRepository)
    {
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Future:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Future:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Future, string>> PickerOrderSelector
    {
        get { return x => x.Code; }   // kullanılmaz — picker composite sırayı tabandaki override kurar
    }

    protected override Guid FollowingUnitIdOf(Future entity)
    {
        return entity.FollowingUnitId;
    }

    protected override decimal CompositeFactorOf(Future entity)
    {
        return entity.FollowingFactor;
    }

    protected override string CodeOf(Future entity)
    {
        return entity.Code;
    }

    protected override Task<Future> MapToEntityAsync(FutureCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Future(createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value, createInput.FollowingFactor);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task MapToEntityAsync(FutureUpdateDto updateInput, Future entity)
    {
        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFollowingFactor(updateInput.FollowingFactor);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
        return Task.CompletedTask;
    }
}
