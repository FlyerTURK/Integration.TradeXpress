using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Application;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülke kataloğu CRUD. Merkezi referans: <b>host global listeyi yönetir, tenant'lar görür</b>.
/// Görünürlük/guard davranışı <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından. Liste zenginleştirmesi: DefaultCurrencyUnitId (id-only referans) → CurrencyUnit.Code çözümü (görüntü kolonu).
/// </summary>
[Authorize(TradeXpressPermissions.Countries.Default)]
public class CountryAppService
    : HostCatalogCrudAppService<Country, CountryGetDto, CountryListDto, CountryListRequestDto, CountryCreateDto, CountryUpdateDto>,
      ICountryAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (Id→Code görüntü çözümü + görünürlük doğrulaması)

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

    protected override async Task<Country> MapToEntityAsync(CountryCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor. Birim id'si görünürlük doğrulamasından geçer.
        var unitId = await EnsureUnitVisibleAsync(createInput.DefaultCurrencyUnitId);
        return new Country(
            createInput.Code, createInput.Name, unitId, createInput.DisplayOrder);
    }

    protected override async Task MapToEntityAsync(CountryUpdateDto updateInput, Country entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); ISO-2 normalize entity kuralıyla aynı (NormalizeInvariantCode).
        // Benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı — TenantId'yi standart filter verir.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeInvariantCode(
                raw, nameof(Country.Code), CountryConsts.CodeMaxLength, CountryConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code,
            "TradeXpress:Country:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetDefaultCurrencyUnit(await EnsureUnitVisibleAsync(updateInput.DefaultCurrencyUnitId));
        entity.SetDisplayOrder(updateInput.DisplayOrder);
        entity.SetActive(updateInput.IsActive);
    }

    // Country için Mapperly entity→DTO mapping yok; DTO'lar elle kurulur (IsGlobal'i taban set eder).
    protected override CountryListDto MapToGetListOutputDto(Country entity)
    {
        return new CountryListDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            DefaultCurrencyUnitId = entity.DefaultCurrencyUnitId,
            IsActive = entity.IsActive,
            DisplayOrder = entity.DisplayOrder,
        };
    }

    protected override async Task<CountryGetDto> MapToGetOutputDtoAsync(Country entity)
    {
        return new CountryGetDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            DefaultCurrencyUnitId = entity.DefaultCurrencyUnitId,
            DefaultCurrencyCode = await LoadUnitCodeAsync(entity.DefaultCurrencyUnitId),
            IsActive = entity.IsActive,
            DisplayOrder = entity.DisplayOrder,
            IsGlobal = entity.TenantId == null,
        };
    }

    // DefaultCurrencyUnitId (id-only referans) → görüntü kolonu için CurrencyUnit.Code'u id'den çöz
    // (bellekte; bu sayfanın id'leri). Kapsam kısıtı gerekmez: id zaten yazımda görünürlükten doğrulanır.
    protected override async Task EnrichListAsync(List<Country> entities, List<CountryListDto> dtos)
    {
        var unitIds = dtos
            .Where(d => d.DefaultCurrencyUnitId.HasValue)
            .Select(d => d.DefaultCurrencyUnitId!.Value)
            .Distinct()
            .ToList();
        if (unitIds.Count == 0)
        {
            return;
        }

        Dictionary<Guid, string> codeById;
        using (DataFilter.Disable<IMultiTenant>())
        {
            var matched = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => unitIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Code }));
            codeById = matched.ToDictionary(u => u.Id, u => u.Code);
        }

        foreach (var dto in dtos)
        {
            if (dto.DefaultCurrencyUnitId is { } uid && codeById.TryGetValue(uid, out var code))
            {
                dto.DefaultCurrencyCode = code;
            }
        }
    }

    /// <summary>Birim görünür mü (global + own); değilse hata. Boş/null id fail-fast reddedilir
    /// (varsayılan birim zorunlu — id-only geçişte otoriter alan DefaultCurrencyUnitId'dir).</summary>
    private async Task<Guid> EnsureUnitVisibleAsync(Guid? unitId)
    {
        if (unitId is not { } id || id == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Country:DefaultCurrencyRequired");
        }

        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var q = (await _unitRepository.GetQueryableAsync())
                .Where(u => u.Id == id && (u.TenantId == null || u.TenantId == tenantId));
            if (!await AsyncExecuter.AnyAsync(q))
            {
                throw new EntityNotFoundException(typeof(CurrencyUnit), id);
            }
        }

        return id;
    }

    /// <summary>Birim kodunu id'den çözer (görüntü alanı; null → null).</summary>
    private async Task<string?> LoadUnitCodeAsync(Guid? unitId)
    {
        if (unitId is not { } id)
        {
            return null;
        }

        using (DataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == id)
                    .Select(u => u.Code));
        }
    }
}
