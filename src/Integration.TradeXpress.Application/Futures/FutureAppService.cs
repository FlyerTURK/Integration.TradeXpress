using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Integration.TradeXpress.Vouchers;

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
    private readonly ICurrentCompany _currentCompany;

    public FutureAppService(
        IRepository<Future, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        ICurrentCompany currentCompany)
        : base(repository, unitRepository)
    {
        _currentCompany = currentCompany;
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Futures.Create;
        UpdatePolicyName = TradeXpressPermissions.Futures.Update;
        DeletePolicyName = TradeXpressPermissions.Futures.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Future entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Future; }
    }

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

    protected override Expression<Func<Future, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Future>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override Task<Future> MapToEntityAsync(FutureCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard).
        var entity = new Future(
            createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value,
            CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany),
            createInput.FollowingFactor);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Future entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Future:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(FutureUpdateDto updateInput, Future entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Future.Code), EntityFieldConsts.CodeMinLength, FutureConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Future:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFollowingFactor(updateInput.FollowingFactor);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    /// <summary>Vadelinin ürün projeksiyonu — iş <see cref="CommodityToProductProjector"/>'da; burada yalnız kaydı
    /// okuma + [Authorize] denetimi (mamüldeki <c>GoodAppService.ProjectToProductAsync</c> ile birebir simetrik).
    ///
    /// <para><b>Şekil <c>Family</c>'den okunur:</b> aile bu sınıfta ZATEN beyanlıdır; ikinci kez yazılsaydı
    /// iki beyan zamanla ayrışabilir ve projeksiyon sessizce yanlış kolu çalıştırırdı (connascence).</para></summary>
    public virtual async Task<ProductGetDto> ProjectToProductAsync(Guid futureId)
    {
        var entity = await Repository.FindAsync(futureId)
            ?? throw new BusinessException("TradeXpress:Future:NotFound");

        return await CommodityToProduct.ProjectAsync(new CommodityProjectionSource(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            CommodityProjectionShapes.Of(Family)));
    }
}
