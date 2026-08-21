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
    private readonly ICurrentCompany _currentCompany;

    public ScrapAppService(
        IRepository<Scrap, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        ICurrentCompany currentCompany)
        : base(repository, unitRepository)
    {
        _currentCompany = currentCompany;
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Scraps.Create;
        UpdatePolicyName = TradeXpressPermissions.Scraps.Update;
        DeletePolicyName = TradeXpressPermissions.Scraps.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Scrap entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Scrap; }
    }

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

    protected override Expression<Func<Scrap, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Scrap>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override Task<Scrap> MapToEntityAsync(ScrapCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard).
        var entity = new Scrap(
            createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value,
            CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany),
            createInput.Factor, createInput.FactorChange);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Scrap entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Scrap:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(ScrapUpdateDto updateInput, Scrap entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Scrap.Code), EntityFieldConsts.CodeMinLength, ScrapConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Scrap:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFactor(updateInput.Factor);
        entity.SetFactorChange(updateInput.FactorChange);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    /// <summary>Hurdanın ürün projeksiyonu — iş <see cref="CommodityToProductProjector"/>'da; burada yalnız kaydı
    /// okuma + [Authorize] denetimi (mamüldeki <c>GoodAppService.ProjectToProductAsync</c> ile birebir simetrik).
    ///
    /// <para><b>Şekil <c>Family</c>'den okunur:</b> aile bu sınıfta ZATEN beyanlıdır; ikinci kez yazılsaydı
    /// iki beyan zamanla ayrışabilir ve projeksiyon sessizce yanlış kolu çalıştırırdı (connascence).</para></summary>
    public virtual async Task<ProductGetDto> ProjectToProductAsync(Guid scrapId)
    {
        var entity = await Repository.FindAsync(scrapId)
            ?? throw new BusinessException("TradeXpress:Scrap:NotFound");

        return await CommodityToProduct.ProjectAsync(new CommodityProjectionSource(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            CommodityProjectionShapes.Of(Family)));
    }
}
