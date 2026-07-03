using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu CRUD. Merkezi referans: <b>host global listeyi yönetir, tenant'lar görür</b>.
/// Görünürlük/guard davranışı <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından. Liste zenginleştirmesi: DefaultCurrencyCode (string, FK yok) → CurrencyUnit.Id çözümü (link için).
/// </summary>
[Authorize(TradeXpressPermissions.Countries.Default)]
public class CountryAppService
    : HostCatalogCrudAppService<Country, CountryGetDto, CountryListDto, CountryListRequestDto, CountryCreateDto, CountryUpdateDto>,
      ICountryAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (DefaultCurrencyCode→Id, link)

    public CountryAppService(
        IRepository<Country, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository)
    {
        _unitRepository = unitRepository;
        LocalizationResource = typeof(TradeXpressResource);
        CreatePolicyName = TradeXpressPermissions.Countries.Create;
        UpdatePolicyName = TradeXpressPermissions.Countries.Update;
        DeletePolicyName = TradeXpressPermissions.Countries.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "DisplayOrder", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Country:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        // Country'de silme de düzenleme guard'ından geçer (ayrı Delete anahtarı lokalize edilmedi) —
        // mevcut davranış/lokalizasyon korunur.
        get { return "TradeXpress:Country:CannotEditGlobalAsTenant"; }
    }

    protected override Expression<Func<Country, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    protected override Task<Country> MapToEntityAsync(CountryCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor.
        return Task.FromResult(new Country(
            createInput.Code, createInput.Name, createInput.DefaultCurrencyCode, createInput.DisplayOrder));
    }

    protected override Task MapToEntityAsync(CountryUpdateDto updateInput, Country entity)
    {
        entity.SetName(updateInput.Name);
        entity.SetDefaultCurrencyCode(updateInput.DefaultCurrencyCode);
        entity.SetDisplayOrder(updateInput.DisplayOrder);
        entity.SetActive(updateInput.IsActive);
        return Task.CompletedTask;
    }

    // Country için Mapperly entity→DTO mapping yok; DTO'lar elle kurulur (IsGlobal'i taban set eder).
    protected override CountryListDto MapToGetListOutputDto(Country entity)
    {
        return new CountryListDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            DefaultCurrencyCode = entity.DefaultCurrencyCode,
            IsActive = entity.IsActive,
            DisplayOrder = entity.DisplayOrder,
        };
    }

    protected override Task<CountryGetDto> MapToGetOutputDtoAsync(Country entity)
    {
        return Task.FromResult(new CountryGetDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            DefaultCurrencyCode = entity.DefaultCurrencyCode,
            IsActive = entity.IsActive,
            DisplayOrder = entity.DisplayOrder,
            IsGlobal = entity.TenantId == null,
        });
    }

    // DefaultCurrencyCode string kolon (FK yok) → linklenebilmesi için CurrencyUnit.Id'yi Code'dan
    // çöz (bellekte; bu sayfanın kodları). Kapsam: global (TenantId=null) + mevcut tenant.
    protected override async Task EnrichListAsync(List<Country> entities, List<CountryListDto> dtos)
    {
        var ccyCodes = entities.Select(c => c.DefaultCurrencyCode).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        if (ccyCodes.Count == 0)
        {
            return;
        }

        var tenantId = CurrentTenant.Id;
        var unitMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        using (DataFilter.Disable<IMultiTenant>())
        {
            var matched = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => ccyCodes.Contains(u.Code) && (u.TenantId == null || u.TenantId == tenantId)));
            foreach (var u in matched)
            {
                unitMap[u.Code] = u.Id;
            }
        }

        foreach (var dto in dtos)
        {
            if (!string.IsNullOrEmpty(dto.DefaultCurrencyCode) && unitMap.TryGetValue(dto.DefaultCurrencyCode, out var uid))
            {
                dto.DefaultCurrencyUnitId = uid;
            }
        }
    }
}
