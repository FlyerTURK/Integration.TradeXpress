using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Application;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Commodities;

namespace Integration.TradeXpress.Services;

/// <summary>
/// Service (Hizmet) CRUD. Görünürlük/guard/picker davranışı <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından gelir: host kataloğu (TenantId=null) herkese görünür + tenant kendi kayıtlarını görür;
/// tenant global kaydı düzenleyemez/silemez.
/// </summary>
[Authorize]
public class ServiceAppService
    : CommodityCatalogAppService<Service, ServiceGetDto, ServiceListDto, ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto>,
      IServiceAppService
{
    private readonly ICurrentCompany _currentCompany;

    public ServiceAppService(
        IRepository<Service, Guid> repository,
        ICurrentCompany currentCompany)
        : base(repository)
    {
        _currentCompany = currentCompany;
        LocalizationResource = typeof(TradeXpressResource);

        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Services.Create;
        UpdatePolicyName = TradeXpressPermissions.Services.Update;
        DeletePolicyName = TradeXpressPermissions.Services.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Service entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Service; }
    }

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Service:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Service:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Service, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual Task<List<ServiceListDto>> GetPickerListAsync()
    {
        return GetPickerListCoreAsync();
    }

    protected override Expression<Func<Service, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Service>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override Task<Service> MapToEntityAsync(ServiceCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard).
        var entity = new Service(
            createInput.Code, createInput.Name,
            CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany));
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Service entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Service:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(ServiceUpdateDto updateInput, Service entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Service.Code), EntityFieldConsts.CodeMinLength, ServiceConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Service:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    /// <summary>Hizmetin ürün projeksiyonu — iş <see cref="CommodityToProductProjector"/>'da; burada yalnız kaydı
    /// okuma + [Authorize] denetimi (mamüldeki <c>GoodAppService.ProjectToProductAsync</c> ile birebir simetrik).
    ///
    /// <para><b>Şekil <c>Family</c>'den okunur:</b> aile bu sınıfta ZATEN beyanlıdır; ikinci kez yazılsaydı
    /// iki beyan zamanla ayrışabilir ve projeksiyon sessizce yanlış kolu çalıştırırdı (connascence).</para></summary>
    public virtual async Task<ProductGetDto> ProjectToProductAsync(Guid serviceId)
    {
        var entity = await Repository.FindAsync(serviceId)
            ?? throw new BusinessException("TradeXpress:Service:NotFound");

        return await CommodityToProduct.ProjectAsync(new CommodityProjectionSource(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            CommodityProjectionShapes.Of(Family)));
    }
}
