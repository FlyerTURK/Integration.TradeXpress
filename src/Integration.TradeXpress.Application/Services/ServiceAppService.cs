using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.TradeXpress.Localization;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Services;

/// <summary>
/// Service (Hizmet) CRUD. Görünürlük/guard/picker davranışı <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından gelir: host kataloğu (TenantId=null) herkese görünür + tenant kendi kayıtlarını görür;
/// tenant global kaydı düzenleyemez/silemez.
/// </summary>
[Authorize]
public class ServiceAppService
    : HostCatalogCrudAppService<Service, ServiceGetDto, ServiceListDto, ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto>,
      IServiceAppService
{
    public ServiceAppService(IRepository<Service, Guid> repository)
        : base(repository)
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

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

    protected override Task<Service> MapToEntityAsync(ServiceCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Service(createInput.Code, createInput.Name);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task MapToEntityAsync(ServiceUpdateDto updateInput, Service entity)
    {
        entity.SetName(updateInput.Name);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
        return Task.CompletedTask;
    }
}
