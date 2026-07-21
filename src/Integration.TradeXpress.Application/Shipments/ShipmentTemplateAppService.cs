using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Birleşik ERP kargo şablonu CRUD — <b>company-owned</b> katalog (kanal-nötr çekirdek). Kapsam DAİMA çalışılan şirket
/// (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId GÖNDERMEZ). Standart kimlik (Code uppercase
/// normalize; Create+Update simetrik benzersizlik ön-kontrolü) + menşei/iade adresi (Address VO) + ücret modeli.
/// Combo için <see cref="GetPickerListAsync"/>. Silme, referans veren ürün YA DA kanal kargo şablonu (K1 köprüsü)
/// varsa <see cref="EnsureNotInUseAsync"/> ile engellenir (AddOn deseni).
/// </summary>
[Authorize(TradeXpressPermissions.ShipmentTemplates.Default)]
public class ShipmentTemplateAppService : TradeXpressAppService, IShipmentTemplateAppService
{
    private readonly IRepository<ShipmentTemplate, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;                       // yalnız OKUMA — silme "kullanımda" guard'ı
    private readonly IRepository<N11ShipmentTemplate, Guid> _n11TemplateRepository;       // yalnız OKUMA — silme guard'ı (K1 köprüsü)
    private readonly IRepository<Carrier, Guid> _carrierRepository;                       // yalnız OKUMA — picker id → firma çözümü (host-global)
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "CarrierName", "FeeModel", "IsActive", "Id" };

    public ShipmentTemplateAppService(
        IRepository<ShipmentTemplate, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<N11ShipmentTemplate, Guid> n11TemplateRepository,
        IRepository<Carrier, Guid> carrierRepository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _productRepository = productRepository;
        _n11TemplateRepository = n11TemplateRepository;
        _carrierRepository = carrierRepository;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<ShipmentTemplateListDto>> GetListAsync(ShipmentTemplateListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<ShipmentTemplateListDto>(0, new List<ShipmentTemplateListDto>());
        }

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var dtos = items.Select(e => ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateListDto>(e)).ToList();
        return new PagedResultDto<ShipmentTemplateListDto>(totalCount, dtos);
    }

    public virtual async Task<ShipmentTemplateGetDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateGetDto>(await _repository.GetAsync(id));
    }

    [Authorize(TradeXpressPermissions.ShipmentTemplates.Create)]
    public virtual async Task<ShipmentTemplateGetDto> CreateAsync(ShipmentTemplateCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");
        }

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu şablon → dostane hata.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(ShipmentTemplate.Code), EntityFieldConsts.CodeMinLength, ShipmentTemplateConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new ShipmentTemplate(
            companyId,
            input.Code,
            input.Name,
            ToAddress(input.OriginAddress),
            input.ProcessingDaysMin,
            input.ProcessingDaysMax);
        ApplyEditable(entity, input);
        await ApplyCarrierAsync(entity, input.CarrierId);

        await _repository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateGetDto>(entity);
    }

    [Authorize(TradeXpressPermissions.ShipmentTemplates.Update)]
    public virtual async Task<ShipmentTemplateGetDto> UpdateAsync(Guid id, ShipmentTemplateUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetOrigin(ToAddress(input.OriginAddress));
        entity.SetProcessingDays(input.ProcessingDaysMin, input.ProcessingDaysMax);
        ApplyEditable(entity, input);
        await ApplyCarrierAsync(entity, input.CarrierId);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateGetDto>(entity);
    }

    [Authorize(TradeXpressPermissions.ShipmentTemplates.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await EnsureNotInUseAsync(id);
        await _repository.DeleteAsync(id);
    }

    public virtual async Task<List<ShipmentTemplateListDto>> GetPickerListAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ShipmentTemplateListDto>();
        }

        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.IsActive)
                .OrderBy(x => x.Name));
        return rows.Select(e => ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateListDto>(e)).ToList();
    }

    /// <summary>Create+Update ortak düzenlenebilir alanları uygular (entity setterları fail-fast + normalize eder).
    /// Code/Name/Origin/ProcessingDays/IsActive çağrı yerinde ayrıca ele alınır (benzersizlik/set-once nedeniyle).</summary>
    private static void ApplyEditable(ShipmentTemplate entity, IShipmentTemplateInput input)
    {
        entity.SetDescription(input.Description);
        entity.SetFee(input.FeeModel, input.ConditionalThreshold, input.ConditionalUnit);
        entity.SetDeliveryDays(input.DeliveryDaysMin, input.DeliveryDaysMax);
        // Kargo firması ApplyCarrierAsync ile ayrı ele alınır (id → firma çözümü async repo okuması gerektirir).
        entity.SetReturn(
            input.ReturnAccepted,
            input.ReturnAddress is null ? null : ToAddress(input.ReturnAddress),
            input.ReturnInfo);
        entity.SetMaxPurchaseQuantity(input.MaxPurchaseQuantity);
    }

    /// <summary>Kargo firması picker'ından gelen id'yi çözer + entity'ye ATOMİK uygular (id + denorm ad snapshot).
    /// id null → firma temizlenir (<see cref="ShipmentTemplate.SetCarrier"/> null,null). id dolu → çekirdek
    /// Carrier (host-global) okunur; snapshot ad SERVER'da çözülen firma adından türetilir (client adı yetkili
    /// değil, SSOT). Carrier IMultiTenant değil → host bağlamı ayrıca zorlanmaz (tenant filtresi yok).</summary>
    private async Task ApplyCarrierAsync(ShipmentTemplate entity, Guid? carrierId)
    {
        if (carrierId is not { } id)
        {
            entity.SetCarrier(null, null);
            return;
        }

        var carrier = await _carrierRepository.GetAsync(id);
        entity.SetCarrier(carrier.Id, carrier.Name);
    }

    /// <summary>Silme guard'ı: şablon bir ürün (<c>Product.ShipmentTemplateId</c>) YA DA bir kanal kargo şablonu
    /// (<c>N11ShipmentTemplate.ShipmentTemplateId</c> — K1 köprüsü) tarafından referans ediliyorsa silinemez
    /// (dangling-ref önlemi; id-only referans, sert FK yok). Aynı şirket kapsamı (auto-filter).</summary>
    private async Task EnsureNotInUseAsync(Guid id)
    {
        var usedByProduct = await AsyncExecuter.AnyAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => p.ShipmentTemplateId == id));

        var usedByChannelTemplate = !usedByProduct && await AsyncExecuter.AnyAsync(
            (await _n11TemplateRepository.GetQueryableAsync())
                .Where(t => t.ShipmentTemplateId == id));

        if (usedByProduct || usedByChannelTemplate)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:InUse");
        }
    }

    /// <summary>Kod değişikliği: normalize → değiştiyse AYNI ŞİRKET altında benzersizliği doğrula (kendisi hariç) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(ShipmentTemplate entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, ShipmentTemplateConsts.CodeMaxLength);
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
                .Where(t => t.CompanyId == companyId && t.Id != excludeId && t.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:CodeAlreadyExists");
        }
    }

    /// <summary>Şablon adres DTO'sunu yeniden-kullanılabilir <see cref="Address"/> VO'ya çevirir (normalize/validasyon VO ctor'unda).</summary>
    private static Address ToAddress(ShipmentAddressDto dto)
    {
        return new Address(
            dto.City,
            dto.Line,
            dto.District,
            dto.Neighborhood,
            dto.PostalCode,
            dto.CountryCode,
            dto.Title,
            dto.CityCode,
            dto.DistrictCode,
            dto.AdministrativeAreaId,
            dto.LocalityId,
            dto.AdministrativeAreaIsoCode);
    }
}
