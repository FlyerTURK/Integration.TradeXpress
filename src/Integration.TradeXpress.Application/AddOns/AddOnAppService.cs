using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.AddOns;

/// <summary>
/// AddOn (sipariş eklentisi) CRUD — <b>company-owned</b> katalog. Kapsam DAİMA çalışılan şirket
/// (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId GÖNDERMEZ). Standart kimlik
/// (Code uppercase normalize; Create+Update simetrik benzersizlik ön-kontrolü) + Price/CurrencyUnit (ZORUNLU).
/// Combo için <see cref="GetPickerListAsync"/>.
/// </summary>
[Authorize(TradeXpressPermissions.AddOns.Default)]
public class AddOnAppService : TradeXpressAppService, IAddOnAppService
{
    private readonly IRepository<AddOn, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;   // yalnız OKUMA — para birimi kodu enrich
    private readonly IRepository<Product, Guid> _productRepository;     // yalnız OKUMA — silme "kullanımda" guard'ı
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "Price", "IsActive", "DisplayOrder", "Id" };

    public AddOnAppService(
        IRepository<AddOn, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Product, Guid> productRepository,
        IDataFilter dataFilter,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _productRepository = productRepository;
        _dataFilter = dataFilter;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<AddOnListDto>> GetListAsync(AddOnListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<AddOnListDto>(0, new List<AddOnListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var dtos = items.Select(e => ObjectMapper.Map<AddOn, AddOnListDto>(e)).ToList();
        await EnrichCurrencyCodesAsync(dtos);
        return new PagedResultDto<AddOnListDto>(totalCount, dtos);
    }

    public virtual async Task<AddOnGetDto> GetAsync(Guid id)
        => ObjectMapper.Map<AddOn, AddOnGetDto>(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.AddOns.Create)]
    public virtual async Task<AddOnGetDto> CreateAsync(AddOnCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu eklenti → dostane hata.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(AddOn.Code), EntityFieldConsts.CodeMinLength, AddOnConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var e = new AddOn(companyId, input.Code, input.Name, input.CurrencyUnitId, input.Price, input.DisplayOrder);
        e.SetDescription(input.Description);
        await _repository.InsertAsync(e, autoSave: true);
        return ObjectMapper.Map<AddOn, AddOnGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.AddOns.Update)]
    public virtual async Task<AddOnGetDto> UpdateAsync(Guid id, AddOnUpdateDto input)
    {
        var e = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(e, input.Code);
        e.SetName(input.Name);
        e.SetCurrencyUnit(input.CurrencyUnitId);
        e.SetPrice(input.Price);
        e.SetDescription(input.Description);
        e.SetDisplayOrder(input.DisplayOrder);
        e.SetActive(input.IsActive);
        await _repository.UpdateAsync(e, autoSave: true);
        return ObjectMapper.Map<AddOn, AddOnGetDto>(e);
    }

    [Authorize(TradeXpressPermissions.AddOns.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await EnsureNotInUseAsync(id);
        await _repository.DeleteAsync(id);
    }

    /// <summary>Silme guard'ı: eklenti bir ürünün "Seçenekler"ine atanmışsa silinemez (dangling-ref önlemi;
    /// ProductAddOn id-only referans, FK yok). Aynı şirket kapsamı (company-owned auto-filter).</summary>
    private async Task EnsureNotInUseAsync(Guid id)
    {
        var inUse = await AsyncExecuter.AnyAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => p.AddOns.Any(a => a.AddOnId == id)));
        if (inUse)
        {
            throw new BusinessException("TradeXpress:AddOn:InUse");
        }
    }

    // Liste DTO'larına para birimi kodunu doldurur (grid gösterimi; entity id-only tuttuğundan Mapperly dolduramaz).
    private async Task EnrichCurrencyCodesAsync(List<AddOnListDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var codes = await LoadCurrencyCodesAsync(dtos.Select(d => d.CurrencyUnitId));
        foreach (var dto in dtos)
        {
            dto.CurrencyUnitCode = codes.TryGetValue(dto.CurrencyUnitId, out var code) ? code : null;
        }
    }

    // Para birimi id → Code (host+tenant scoped → IMultiTenant filtresi kapatılır; AccountAppService deseni).
    private async Task<Dictionary<Guid, string>> LoadCurrencyCodesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (list.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _unitRepository.GetQueryableAsync()).Where(u => list.Contains(u.Id)));
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    /// <summary>Kod değişikliği: normalize → değiştiyse AYNI ŞİRKET altında benzersizliği doğrula (kendisi hariç) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(AddOn entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, AddOnConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId, CompanyId, Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty, Update'te entity.Id. Dostane BusinessException.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(a => a.CompanyId == companyId && a.Id != excludeId && a.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:AddOn:CodeAlreadyExists");
        }
    }

    public virtual async Task<List<AddOnListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
            return new List<AddOnListDto>();

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));
        return rows.Select(e => ObjectMapper.Map<AddOn, AddOnListDto>(e)).ToList();
    }
}
