using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Shipments;

/// <summary>
/// Birleşik ERP kargo şablonu CRUD — <b>company-owned</b> katalog (kanal-nötr çekirdek). Kapsam DAİMA çalışılan şirket
/// (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId GÖNDERMEZ). Standart kimlik (Code uppercase
/// normalize; Create+Update simetrik benzersizlik ön-kontrolü) + gönderim/iade adresi (ŞUBE ya da özel Address VO;
/// şube doğrulanır — geçerli şirkete ait + adresi dolu) + ücret modeli.
/// Combo için <see cref="GetPickerListAsync"/>. Silme, referans veren ürün YA DA kanal kargo şablonu (K1 köprüsü)
/// varsa <see cref="EnsureNotInUseAsync"/> ile engellenir (AddOn deseni).
/// </summary>
[Authorize(TradeXpressPermissions.ShipmentTemplates.Default)]
public class ShipmentTemplateAppService : TradeXpressAppService, IShipmentTemplateAppService
{
    private readonly IRepository<ShipmentTemplate, Guid> _repository;
    private readonly IRepository<Product, Guid> _productRepository;                       // yalnız OKUMA — silme "kullanımda" guard'ı
    private readonly IRepository<N11ShipmentTemplate, Guid> _n11TemplateRepository;       // yalnız OKUMA — silme guard'ı (K1 köprüsü) + kanal dağıtımları
    private readonly IRepository<SalesChannelTrN11, Guid> _n11ChannelRepository;          // yalnız OKUMA — dağıtım listesinde kanal ad çözümü
    private readonly IRepository<Carrier, Guid> _carrierRepository;                       // yalnız OKUMA — picker id → firma çözümü (host-global)
    private readonly IRepository<Branch, Guid> _branchRepository;                         // yalnız OKUMA — gönderim/iade şubesi doğrulama + ad çözümü
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "CarrierName", "FeeModel", "IsActive", "Id" };

    public ShipmentTemplateAppService(
        IRepository<ShipmentTemplate, Guid> repository,
        IRepository<Product, Guid> productRepository,
        IRepository<N11ShipmentTemplate, Guid> n11TemplateRepository,
        IRepository<SalesChannelTrN11, Guid> n11ChannelRepository,
        IRepository<Carrier, Guid> carrierRepository,
        IRepository<Branch, Guid> branchRepository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _productRepository = productRepository;
        _n11TemplateRepository = n11TemplateRepository;
        _n11ChannelRepository = n11ChannelRepository;
        _carrierRepository = carrierRepository;
        _branchRepository = branchRepository;
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
        return await ToGetDtoAsync(await _repository.GetAsync(id));
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

        // Gönderim adresi = ŞUBE (doğrulanır) XOR ÖZEL adres; tam biri (entity invariant zorlar).
        var (dispatchBranchId, dispatchAddress) = await ResolveDispatchAsync(input, companyId);
        var entity = new ShipmentTemplate(
            companyId,
            input.Code,
            input.Name,
            dispatchBranchId,
            dispatchAddress,
            input.ProcessingDaysMin,
            input.ProcessingDaysMax);
        ApplyEditable(entity, input);
        await ApplyReturnAsync(entity, input, companyId);
        await ApplyCarrierAsync(entity, input.CarrierId);

        await _repository.InsertAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.ShipmentTemplates.Update)]
    public virtual async Task<ShipmentTemplateGetDto> UpdateAsync(Guid id, ShipmentTemplateUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);

        // Gönderim adresi = ŞUBE (doğrulanır) XOR ÖZEL adres; tam biri (entity invariant zorlar).
        var (dispatchBranchId, dispatchAddress) = await ResolveDispatchAsync(input, entity.CompanyId);
        entity.SetDispatch(dispatchBranchId, dispatchAddress);
        entity.SetProcessingDays(input.ProcessingDaysMin, input.ProcessingDaysMax);
        ApplyEditable(entity, input);
        await ApplyReturnAsync(entity, input, entity.CompanyId);
        await ApplyCarrierAsync(entity, input.CarrierId);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
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

    public virtual async Task<List<ShipmentTemplateChannelDeploymentDto>> GetChannelDeploymentsAsync(Guid shipmentTemplateId)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ShipmentTemplateChannelDeploymentDto>();
        }

        // Çekirdek şablon çalışılan şirkete ait değilse (ya da yoksa) dağıtım listesi anlamsız → boş liste (GetList deseni).
        var coreOwned = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(t => t.Id == shipmentTemplateId && t.CompanyId == companyId));
        if (!coreOwned)
        {
            return new List<ShipmentTemplateChannelDeploymentDto>();
        }

        // N11 kanal dağıtımları — çekirdeğe K1 köprüsüyle bağlı + aynı şirket (company query-filter yok → elle).
        var n11Deployments = await AsyncExecuter.ToListAsync(
            (await _n11TemplateRepository.GetQueryableAsync())
                .Where(t => t.ShipmentTemplateId == shipmentTemplateId && t.CompanyId == companyId));
        if (n11Deployments.Count == 0)
        {
            return new List<ShipmentTemplateChannelDeploymentDto>();
        }

        // Kanal adları id'lerden çözülür (denormalize; salt görüntü) — çekirdek ad-çözüm deseniyle hizalı.
        var channelNames = await ResolveN11ChannelNamesAsync(n11Deployments.Select(t => t.SalesChannelId));

        // Yeni kanal aileleri geldiğinde (Trendyol/Etsy) burada kendi dağıtımları eklenir; DTO SalesChannelType ile ayrışır.
        return n11Deployments
            .Select(t => new ShipmentTemplateChannelDeploymentDto
            {
                SalesChannelType = SalesChannelType.TrN11,
                SalesChannelId = t.SalesChannelId,
                SalesChannelName = channelNames.GetValueOrDefault(t.SalesChannelId, string.Empty),
                ChannelTemplateId = t.Id,
                ChannelTemplateName = t.TemplateName,
            })
            .OrderBy(d => d.SalesChannelName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.ChannelTemplateName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>N11 kanal id'lerini adlarına çözer (denormalize; salt görüntü). Silinmiş/bulunamayan kanal sözlükte yer
    /// almaz → çağıran boş ada düşer. Çalışılan şirket kapsamı auto-filter (per-tenant) + çekirdek zaten şirkete ait.</summary>
    private async Task<Dictionary<Guid, string>> ResolveN11ChannelNamesAsync(IEnumerable<Guid> channelIds)
    {
        var ids = channelIds.Distinct().ToList();
        var channels = await AsyncExecuter.ToListAsync(
            (await _n11ChannelRepository.GetQueryableAsync()).Where(c => ids.Contains(c.Id)));
        return channels.ToDictionary(c => c.Id, c => c.Name);
    }

    /// <summary>Create+Update ortak düzenlenebilir alanları uygular (entity setterları fail-fast + normalize eder).
    /// Code/Name/Dispatch/ProcessingDays/IsActive çağrı yerinde, Carrier + Return async helper'larda ayrıca ele alınır
    /// (benzersizlik/set-once/şube çözümü nedeniyle).</summary>
    private static void ApplyEditable(ShipmentTemplate entity, IShipmentTemplateInput input)
    {
        entity.SetDescription(input.Description);
        entity.SetFee(input.FeeModel, input.ConditionalThreshold, input.ConditionalUnit);
        entity.SetDeliveryDays(input.DeliveryDaysMin, input.DeliveryDaysMax);
        entity.SetMaxPurchaseQuantity(input.MaxPurchaseQuantity);
    }

    /// <summary>Gönderim adresini çözer: şube modu (<c>DispatchBranchId</c> dolu) → şube doğrulanır + (branchId, null);
    /// özel-adres modu → (null, VO). İkisi de boş → VO null bırakılır ve entity <c>SetDispatch</c> "tam biri" invariant'ı
    /// fail-fast eder. Özel adres ÜLKE SERBEST (kilit yok — sınır-ötesi senaryo).</summary>
    private async Task<(Guid? BranchId, Address? Address)> ResolveDispatchAsync(IShipmentTemplateInput input, Guid companyId)
    {
        if (input.DispatchBranchId is { } branchId && branchId != Guid.Empty)
        {
            await EnsureBranchUsableAsync(branchId, companyId);
            return (branchId, null);
        }

        return (null, input.DispatchAddress is null ? null : ToAddress(input.DispatchAddress));
    }

    /// <summary>İade bilgisini çözer + entity'ye uygular. İade kapalı VEYA "gönderimle aynı" → şube/adres yok (null,null;
    /// entity temizler). Farklı iade → şube modu (doğrulanır) XOR özel adres (tam biri; entity invariant zorlar).</summary>
    private async Task ApplyReturnAsync(ShipmentTemplate entity, IShipmentTemplateInput input, Guid companyId)
    {
        Guid? branchId = null;
        Address? address = null;

        if (input.ReturnAccepted && !input.ReturnSameAsDispatch)
        {
            if (input.ReturnBranchId is { } returnBranchId && returnBranchId != Guid.Empty)
            {
                await EnsureBranchUsableAsync(returnBranchId, companyId);
                branchId = returnBranchId;
            }
            else if (input.ReturnAddress is not null)
            {
                address = ToAddress(input.ReturnAddress);
            }
        }

        entity.SetReturn(input.ReturnAccepted, input.ReturnSameAsDispatch, branchId, address, input.ReturnInfo);
    }

    /// <summary>Gönderim/iade şubesini doğrular: şube MEVCUT + GEÇERLİ şirkete ait (aksi → <c>BranchInvalid</c>) +
    /// posta adresi DOLU (adressiz şubeden gönderim/iade anlamsız → <c>BranchAddressMissing</c>). Branch per-tenant
    /// (auto-filter) → yalnız aktif tenant'ın şubesi bulunur; şirket eşleşmesi ayrıca doğrulanır (company-owned sınır).</summary>
    private async Task EnsureBranchUsableAsync(Guid branchId, Guid companyId)
    {
        var branch = await _branchRepository.FindAsync(branchId);
        if (branch is null || branch.CompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:BranchInvalid");
        }

        if (branch.Address is null)
        {
            throw new BusinessException("TradeXpress:Shipment:Template:BranchAddressMissing");
        }
    }

    /// <summary>Entity → GetDto (Mapperly) + denormalize şube adlarını (salt görüntü) id'lerden çözer.</summary>
    private async Task<ShipmentTemplateGetDto> ToGetDtoAsync(ShipmentTemplate entity)
    {
        var dto = ObjectMapper.Map<ShipmentTemplate, ShipmentTemplateGetDto>(entity);
        dto.DispatchBranchName = await ResolveBranchNameAsync(entity.DispatchBranchId);
        dto.ReturnBranchName = await ResolveBranchNameAsync(entity.ReturnBranchId);
        return dto;
    }

    /// <summary>Şube id → ad (salt görüntü). null id → null. Silinmiş/bulunamayan → null (görüntü zarif düşer).</summary>
    private async Task<string?> ResolveBranchNameAsync(Guid? branchId)
    {
        if (branchId is not { } id)
        {
            return null;
        }

        var branch = await _branchRepository.FindAsync(id);
        return branch?.Name;
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
            dto.AdministrativeAreaIsoCode,
            buildingName: dto.BuildingName,
            buildingNumber: dto.BuildingNumber,
            room: dto.Room,
            floor: dto.Floor,
            postbox: dto.Postbox,
            additionalStreetName: dto.AdditionalStreetName);
    }
}
