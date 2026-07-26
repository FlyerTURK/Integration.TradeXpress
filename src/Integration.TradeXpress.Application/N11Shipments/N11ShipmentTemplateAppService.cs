using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Addressing;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo şablonu CRUD — <b>company-owned + per-tenant</b>, kanala bağlı (SalesChannel deseni). Kapsam DAİMA
/// çalışılan şirket (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId GÖNDERMEZ). Şablon bizde tutulur
/// + N11'e push edilir (kanalın KENDİ kimliğiyle). id-only ref'ler (kargo firması ExternalId / il kodu) push'ta
/// host-global referanslardan isimlere ÇÖZÜLÜR; içe aktarımda isim/kod → id ters-çözülür. Host-global okumalar
/// <c>CurrentTenant.Change(null)</c> ile sabitlenir. N11'de silme yok → <see cref="DeleteAsync"/> yalnız yereli siler.
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class N11ShipmentTemplateAppService : TradeXpressAppService, IN11ShipmentTemplateAppService
{
    private readonly IRepository<N11ShipmentTemplate, Guid> _repository;
    private readonly IRepository<SalesChannelTrN11, Guid> _channelRepository;
    private readonly IRepository<N11ShipmentCompany, Guid> _shipmentCompanyRepository;
    private readonly IRepository<N11City, Guid> _cityRepository;
    private readonly IRepository<ShipmentTemplate, Guid> _coreTemplateRepository;   // yalnız OKUMA — FORWARD taslak: çekirdekten ön-doldurma
    private readonly IRepository<Branch, Guid> _branchRepository;                   // yalnız OKUMA — çekirdek şube modunda gönderim adresi çözümü
    private readonly ICurrentCompany _currentCompany;
    private readonly IN11ShipmentTemplateClient _client;
    private readonly IShipmentTemplateReconciler _coreReconciler;   // REVERSE K1 köprüsü — çekirdek bul-veya-oluştur (SRP; çekirdek repo N11 servisinde tutulmaz)

    public N11ShipmentTemplateAppService(
        IRepository<N11ShipmentTemplate, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IRepository<N11ShipmentCompany, Guid> shipmentCompanyRepository,
        IRepository<N11City, Guid> cityRepository,
        IRepository<ShipmentTemplate, Guid> coreTemplateRepository,
        IRepository<Branch, Guid> branchRepository,
        ICurrentCompany currentCompany,
        IN11ShipmentTemplateClient client,
        IShipmentTemplateReconciler coreReconciler)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _shipmentCompanyRepository = shipmentCompanyRepository;
        _cityRepository = cityRepository;
        _coreTemplateRepository = coreTemplateRepository;
        _branchRepository = branchRepository;
        _currentCompany = currentCompany;
        _client = client;
        _coreReconciler = coreReconciler;
    }

    public virtual async Task<List<N11ShipmentTemplateDto>> GetListAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId)
                .OrderBy(x => x.TemplateName));

        return items.Select(x => ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(x)).ToList();
    }

    public virtual async Task<N11ShipmentTemplateDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedTemplateAsync(id);
        return ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<N11ShipmentTemplateDto> CreateAsync(N11ShipmentTemplateCreateDto input)
    {
        var channel = await GetOwnedChannelAsync(input.SalesChannelId);
        EnsureN11Requirements(input);
        var templateName = NormalizeName(input.TemplateName);
        await EnsureTemplateNameUniqueAsync(channel.Id, templateName, Guid.Empty);

        var entity = new N11ShipmentTemplate(
            channel.CompanyId,
            channel.Id,
            templateName,
            input.DeliveryFeeType,
            input.ShipmentMethod,
            ToAddress(input.WarehouseAddress));
        ApplyInput(entity, input);

        // Kaydet = N11 ile SENKRON: önce N11'e yaz (reddederse fırlatır → yerele DE yazılmaz, drift olmaz), sonra yerele.
        await PushToN11Async(entity, channel);
        // REVERSE K1: push başarılı → çekirdeği bul-veya-oluştur + bağla (aynı UoW), sonra tek Insert ile persist.
        await ReconcileCoreTemplateAsync(entity);
        await _repository.InsertAsync(entity, autoSave: true);

        return ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<N11ShipmentTemplateDto> UpdateAsync(Guid id, N11ShipmentTemplateUpdateDto input)
    {
        var entity = await GetOwnedTemplateAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        EnsureN11Requirements(input);
        await EnsureTemplateNameUniqueAsync(entity.SalesChannelId, NormalizeName(input.TemplateName), entity.Id);
        ApplyInput(entity, input);

        // Kaydet = N11 ile SENKRON: önce N11'e yaz, sonra yerele (push başarısız → yerel değişiklik de kalıcı olmaz).
        await PushToN11Async(entity, channel);
        // REVERSE K1: push başarılı → henüz bağlı değilse çekirdeği bul-veya-oluştur + bağla (aynı UoW).
        await ReconcileCoreTemplateAsync(entity);
        await _repository.UpdateAsync(entity, autoSave: true);

        return ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // N11'de silme operasyonu YOK → yerel silme drift yaratır. UI'da silme kapalı; kalırsa yalnız yereli siler
        // (bir sonraki İçe Aktar mutabakatında N11'de duruyorsa geri gelir).
        var entity = await GetOwnedTemplateAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task PushAsync(Guid id)
    {
        var entity = await GetOwnedTemplateAsync(id);
        var channel = await GetOwnedChannelAsync(entity.SalesChannelId);
        await PushToN11Async(entity, channel);
    }

    // Entity'yi N11'e CreateOrUpdate ile gönderir (kanalın kimliğiyle; şartlı kargo dahil). N11 reddederse
    // BusinessException fırlatır → çağıran (Create/Update) yerele yazmaz → yerel = N11 garantisi.
    private async Task PushToN11Async(N11ShipmentTemplate entity, SalesChannelTrN11 channel)
    {
        var companyRefs = await LoadCompanyRefsByExternalIdAsync();
        var cityNames = await LoadCityNamesByCodeAsync();
        var data = ToData(entity, companyRefs, cityNames);
        await _client.CreateOrUpdateAsync(data, channel.AppKey, channel.AppSecret);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<int> SyncAsync(Guid salesChannelId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);
        var templates = await _client.GetTemplateListAsync(channel.AppKey, channel.AppSecret);
        var externalIdByShortName = await LoadExternalIdByShortNameAsync();

        var existing = (await AsyncExecuter.ToListAsync(
                (await _repository.GetQueryableAsync()).Where(x => x.SalesChannelId == channel.Id)))
            .ToDictionary(x => x.TemplateName, StringComparer.Ordinal);

        var changed = 0;
        foreach (var data in templates)
        {
            if (existing.TryGetValue(NormalizeName(data.TemplateName), out var entity))
            {
                ApplyData(entity, data, externalIdByShortName);
                // REVERSE K1: import ShipmentTemplateId'ye DOKUNMAZ → bağlı değilse çekirdeği geriye-doldur (idempotent).
                await ReconcileCoreTemplateAsync(entity);
                await _repository.UpdateAsync(entity, autoSave: true);
            }
            else
            {
                var created = new N11ShipmentTemplate(
                    channel.CompanyId,
                    channel.Id,
                    NormalizeName(data.TemplateName),
                    (N11DeliveryFeeType)data.DeliveryFeeType,
                    (N11ShipmentMethod)data.ShipmentMethod,
                    ToAddress(data.WarehouseAddress));
                ApplyData(created, data, externalIdByShortName);
                // REVERSE K1: yeni içe aktarılan şablon çekirdeğe bağlı değil → bul-veya-oluştur + bağla, sonra tek Insert.
                await ReconcileCoreTemplateAsync(created);
                await _repository.InsertAsync(created, autoSave: true);
            }

            changed++;
        }

        // Tam mutabakat: N11'de artık olmayan yerel şablonları sil → yerel = N11 (drift olmaz).
        var fetchedNames = new HashSet<string>(templates.Select(t => NormalizeName(t.TemplateName)), StringComparer.Ordinal);
        var stale = existing.Values.Where(e => !fetchedNames.Contains(e.TemplateName)).ToList();
        if (stale.Count > 0)
        {
            await _repository.DeleteManyAsync(stale, autoSave: true);
        }

        return changed;
    }

    // ── FORWARD taslak (çekirdek → N11 ön-doldurma) ─────────────────────────────────────────────────

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<N11ShipmentTemplateCreateDto> BuildDeploymentDraftAsync(Guid shipmentTemplateId, Guid salesChannelId)
    {
        // Guard: kanal + çekirdek şablon ikisi de çalışılan şirkete ait (aksi → dostane bulunamadı).
        var channel = await GetOwnedChannelAsync(salesChannelId);
        var core = await GetOwnedCoreTemplateAsync(shipmentTemplateId);
        var dispatchAddress = await ResolveDispatchAddressAsync(core);
        // İade/değişim adresi: çekirdekte AYRI iade adresi yoksa depo (gönderim) adresinin aynısı (kullanıcı kararı).
        var exchangeAddress = await ResolveExchangeAddressAsync(core, dispatchAddress);
        // N11 adres başlığı (title) zorunlu → adreste başlık boşsa şube adından türet (gönderim şube modundaysa).
        var fallbackTitle = await ResolveDispatchBranchNameAsync(core);

        // PERSIST ETMEZ — yalnız ön-doldurulmuş taslak. Kullanıcı zorunlu N11 alanlarını tamamlayıp CreateAsync ile kaydeder
        // (EnsureN11Requirements + push o zaman çalışır). DeliveryFeeType/ShipmentMethod: çekirdek FeeModel→N11 DeliveryFeeType
        // eşlemesi TEMİZ DEĞİL (çekirdek "Free" için N11'de birebir karşılık yok) → eşleme YAPILMAZ; enum'ın ilk tanımlı değeri
        // varsayılan (CLR default 0 her iki enumda da geçersiz) — kullanıcı formda seçer.
        return new N11ShipmentTemplateCreateDto
        {
            SalesChannelId = salesChannelId,
            ShipmentTemplateId = shipmentTemplateId,   // ileri köprü — reverse-reconcile bunu görüp ATLAR (origin-guard)
            TemplateName = core.Name,
            WarehouseAddress = ToAddressDto(dispatchAddress, fallbackTitle),
            // N11 anlaşmalı kargoda deliveryFeeType YALNIZ 2 (mağaza öder) / 3 (şartlı) — 1 (alıcı öder) reddedilir (canlı doğrulandı).
            DeliveryFeeType = N11DeliveryFeeType.SellerPays,     // varsayılan: mağaza öder (2); FeeModel'den TÜRETİLMEZ
            ShipmentMethod = N11ShipmentMethod.Cargo,            // varsayılan (ilk tanımlı değer = kargo)
            UseDmallCargo = true,                               // n11.com anlaşmalı kargo mandası (NewTemplate deseni)
            ConditionalShippingUnit = N11ConditionalShippingUnit.Amount,   // NewTemplate deseni
            ShippingInfo = channel.DefaultShippingInfo,         // kanal düzeyi varsayılan bilgi metinleri (null olabilir)
            ExchangeInfo = channel.DefaultExchangeInfo,
            InstallmentInfo = channel.DefaultInstallmentInfo,
            // İade/değişim adresi = çekirdekte ayrı iade adresi varsa o, yoksa depo adresinin aynısı (ToAddressDto → ayrı DTO örneği).
            ExchangeAddress = ToAddressDto(exchangeAddress, fallbackTitle),
        };
    }

    // ── REVERSE K1 köprüsü (kanal → çekirdek ters mutabakat) ────────────────────────────────────────

    /// <summary>Kanal şablonu kaydedilince aynı ad/kodda çekirdek <c>ShipmentTemplate</c>'i OTOMATİK bul-veya-oluştur
    /// ve bağla. <b>Origin-guard</b>: şablon zaten bir çekirdeğe bağlıysa (<c>ShipmentTemplateId != null</c>) ATLA →
    /// ikinci çekirdek üretilmez (idempotent). Aksi halde reconciler çalışılan şirket kapsamında
    /// <c>Code == NormalizeCode(TemplateName)</c> çekirdeği bulur/oluşturur; dönen id <c>SetCoreTemplate</c> ile yazılır.
    /// Depo adresi → çekirdek gönderim (özel) adresi. N11'e push başarılı olduktan SONRA, aynı UoW içinde çağrılır
    /// (çağıran ardından entity'yi Insert/Update ile persist eder → SetCoreTemplate kalıcı olur).</summary>
    private async Task ReconcileCoreTemplateAsync(N11ShipmentTemplate entity)
    {
        if (entity.ShipmentTemplateId is not null)
        {
            // Zaten bir çekirdeğe bağlı → ters-üretim ATLA (kullanıcı istemli bağladıysa da korunur).
            return;
        }

        var coreTemplateId = await _coreReconciler.FindOrCreateFromChannelAsync(
            entity.CompanyId, entity.TemplateName, entity.WarehouseAddress);
        entity.SetCoreTemplate(coreTemplateId);
    }

    // ── Uygulama (DTO/data → entity) ────────────────────────────────────────────────────────────────

    private void ApplyInput(N11ShipmentTemplate entity, IN11ShipmentTemplateInput input)
    {
        // K1 köprüsü — çekirdek şablon referansı (id-only); N11'e push EDİLMEZ, yalnız yerelde tutulur.
        entity.SetCoreTemplate(input.ShipmentTemplateId);
        entity.SetTemplateName(input.TemplateName);
        entity.SetDeliveryFeeType(input.DeliveryFeeType);
        entity.SetShipmentMethod(input.ShipmentMethod);
        entity.SetFlags(input.SpecialDelivery, input.CombinedShipmentAllowed, input.UseDmallCargo);
        entity.SetInfos(input.ShippingInfo, input.ExchangeInfo, input.InstallmentInfo);
        entity.SetCargoAccountNo(input.CargoAccountNo);
        entity.SetClaimShipmentCompany(input.ClaimShipmentCompanyExternalId);
        entity.SetWarehouseAddress(ToAddress(input.WarehouseAddress));
        entity.SetExchangeAddress(input.ExchangeAddress is null ? null : ToAddress(input.ExchangeAddress));
        entity.SetShipmentCompanies(input.ShipmentCompanyExternalIds);
        entity.SetDeliverableCities(input.DeliverableCityCodes);
        entity.SetConditionalShipping(input.ConditionalShippingThreshold, input.ConditionalShippingUnit);
    }

    // İçe aktarım: N11'den gelen ÇÖZÜLMÜŞ veriyi (isim/kod) id-ref'lere ters-çözer, entity'ye uygular.
    // NOT: Çekirdek şablon referansı (ShipmentTemplateId, K1 köprüsü) N11'de bilinmez → import'ta DOKUNULMAZ (korunur).
    private void ApplyData(N11ShipmentTemplate entity, N11ShipmentTemplateData data, IReadOnlyDictionary<string, string> externalIdByShortName)
    {
        entity.SetTemplateName(data.TemplateName);
        entity.SetDeliveryFeeType((N11DeliveryFeeType)data.DeliveryFeeType);
        entity.SetShipmentMethod((N11ShipmentMethod)data.ShipmentMethod);
        entity.SetFlags(data.SpecialDelivery, data.CombinedShipmentAllowed, data.UseDmallCargo);
        entity.SetInfos(data.ShippingInfo, data.ExchangeInfo, data.InstallmentInfo);
        entity.SetCargoAccountNo(data.CargoAccountNo);
        entity.SetClaimShipmentCompany(data.ClaimShipmentCompany is { } claim ? ResolveExternalId(claim, externalIdByShortName) : null);
        entity.SetWarehouseAddress(ToAddress(data.WarehouseAddress));
        entity.SetExchangeAddress(data.ExchangeAddress is null ? null : ToAddress(data.ExchangeAddress));
        entity.SetShipmentCompanies(data.ShipmentCompanies
            .Select(c => ResolveExternalId(c, externalIdByShortName))
            .Where(x => x is not null)
            .Select(x => x!));
        entity.SetDeliverableCities(data.DeliverableCities.Select(c => c.Code));

        // Şartlı kargo depo adresine gömülü döner → şablon-düzeyine köprüle (push'ta da geri yazılır — canlı doğrulandı).
        entity.SetConditionalShipping(
            data.WarehouseAddress.ConditionalShippingThreshold,
            data.WarehouseAddress.ConditionalShippingUnit ?? N11ConditionalShippingUnit.Amount);
    }

    private static string? ResolveExternalId(N11ShipmentCompanyRef company, IReadOnlyDictionary<string, string> externalIdByShortName)
    {
        // Kısa-kodsuz firma (ShortName null) ters-çözülemez — null anahtarla TryGetValue ArgumentNullException atardı.
        if (string.IsNullOrEmpty(company.ShortName))
        {
            return null;
        }

        return externalIdByShortName.TryGetValue(company.ShortName, out var externalId) ? externalId : null;
    }

    // ── Push (entity → ÇÖZÜLMÜŞ data) ───────────────────────────────────────────────────────────────

    private static N11ShipmentTemplateData ToData(
        N11ShipmentTemplate entity,
        IReadOnlyDictionary<string, N11ShipmentCompanyRef> companyRefs,
        IReadOnlyDictionary<string, string> cityNames)
    {
        var companies = entity.ShipmentCompanyExternalIds
            .Where(companyRefs.ContainsKey)
            .Select(id => companyRefs[id])
            .ToList();

        var cities = entity.DeliverableCityCodes
            .Select(code => new N11ShipmentCityRef(code, cityNames.GetValueOrDefault(code, string.Empty)))
            .ToList();

        N11ShipmentCompanyRef? claim = entity.ClaimShipmentCompanyExternalId is { } claimId
            && companyRefs.TryGetValue(claimId, out var claimRef)
            ? claimRef
            : null;

        return new N11ShipmentTemplateData(
            entity.TemplateName,
            (byte)entity.DeliveryFeeType,
            (byte)entity.ShipmentMethod,
            entity.SpecialDelivery,
            entity.CombinedShipmentAllowed,
            entity.UseDmallCargo,
            entity.ShippingInfo,
            entity.ExchangeInfo,
            entity.InstallmentInfo,
            entity.CargoAccountNo,
            claim,
            ToAddressData(entity.WarehouseAddress, entity.ConditionalShippingThreshold, entity.ConditionalShippingUnit),
            entity.ExchangeAddress is null ? null : ToAddressData(entity.ExchangeAddress, entity.ConditionalShippingThreshold, entity.ConditionalShippingUnit),
            companies,
            cities);
    }

    // Şartlı kargo (feeCondition) N11'de adres elementine gömülü → depo + iade adresine yaz (canlı doğrulandı: push kabul edilir).
    private static N11ShipmentAddressData ToAddressData(Address address, decimal? threshold, N11ConditionalShippingUnit unit)
    {
        return new N11ShipmentAddressData(
            address.Title,
            address.Line,
            address.CityCode ?? string.Empty,
            address.City,
            address.DistrictCode,
            address.District,
            address.PostalCode,
            threshold,
            threshold is null ? null : unit);
    }

    // ── Adres eşleme (DTO / data → VO) ──────────────────────────────────────────────────────────────

    private static Address ToAddress(N11ShipmentAddressDto dto)
    {
        return new Address(
            city: dto.City,
            line: dto.Line,
            district: dto.District,
            neighborhood: dto.Neighborhood,
            postalCode: dto.PostalCode,
            countryCode: string.IsNullOrWhiteSpace(dto.CountryCode) ? "TR" : dto.CountryCode,
            title: dto.Title,
            cityCode: dto.CityCode,
            districtCode: dto.DistrictCode,
            // Coğrafya referansları (additive) — VO'ya taşınır, N11 push OKUMAZ (yalnız zenginleştirme/UBL için).
            administrativeAreaId: dto.AdministrativeAreaId,
            localityId: dto.LocalityId,
            administrativeAreaIsoCode: dto.AdministrativeAreaIsoCode,
            // UBL zenginleştirme alanları (additive) — VO'ya taşınır, N11 push OKUMAZ.
            buildingName: dto.BuildingName,
            buildingNumber: dto.BuildingNumber,
            room: dto.Room,
            floor: dto.Floor,
            postbox: dto.Postbox,
            additionalStreetName: dto.AdditionalStreetName);
    }

    /// <summary>Çekirdek şablonun EFEKTİF gönderim adresini N11 depo adresi DTO'suna çözer (FORWARD taslak). Şube modu
    /// (<c>DispatchBranchId</c> dolu) → şubenin <see cref="Address"/> VO'su; adressiz şube → dostane hata (adressiz şubeden
    /// gönderim anlamsız; çekirdek <c>EnsureBranchUsableAsync</c> ile hizalı). Özel-adres modu → <c>DispatchAddress</c> VO.
    /// Çekirdek "tam biri" invariant'ı gereği biri daima dolu (savunmacı fallback).</summary>
    private async Task<Address> ResolveDispatchAddressAsync(ShipmentTemplate core)
    {
        if (core.DispatchBranchId is { } branchId)
        {
            var branch = await _branchRepository.FindAsync(branchId);
            if (branch?.Address is null)
            {
                throw new BusinessException("TradeXpress:N11:Shipment:DispatchBranchAddressMissing");
            }

            return branch.Address;
        }

        if (core.DispatchAddress is { } customAddress)
        {
            return customAddress;
        }

        // Çekirdek invariant gereği buraya düşülmez (şube XOR özel — tam biri dolu); yine de fail-fast.
        throw new BusinessException("TradeXpress:N11:Shipment:DispatchAddressMissing");
    }

    /// <summary>İade/değişim adresini çözer: çekirdekte AYRI iade adresi seçilmişse (iade kabul + "gönderimle aynı
    /// değil" + şube/özel adres) onu; aksi halde gönderim (depo) adresinin AYNISINI döner — kullanıcı kararı: çekirdekte
    /// farklı iade adresi seçilmemişse iade/değişim adresi = depo adresi. İade şubesinin adresi eksikse depoya düşülür
    /// (savunmacı).</summary>
    private async Task<Address> ResolveExchangeAddressAsync(ShipmentTemplate core, Address dispatchAddress)
    {
        if (core.ReturnAccepted && !core.ReturnSameAsDispatch)
        {
            if (core.ReturnBranchId is { } returnBranchId)
            {
                var branch = await _branchRepository.FindAsync(returnBranchId);
                if (branch?.Address is not null)
                {
                    return branch.Address;
                }
            }
            else if (core.ReturnAddress is { } returnAddress)
            {
                return returnAddress;
            }
        }

        // Çekirdekte farklı iade adresi yok → depo (gönderim) adresinin aynısı.
        return dispatchAddress;
    }

    /// <summary>Gönderim şube modundaysa şubenin adını döner (N11 adres başlığı zorunlu → boş başlık için fallback).
    /// Özel-adres modunda şube yok → null (kullanıcı başlığı elle girer; ön-doğrulama uyarır).</summary>
    private async Task<string?> ResolveDispatchBranchNameAsync(ShipmentTemplate core)
    {
        if (core.DispatchBranchId is { } branchId)
        {
            var branch = await _branchRepository.FindAsync(branchId);
            return branch?.Name;
        }

        return null;
    }

    // Address VO → N11 depo adresi DTO'su (ToAddress(N11ShipmentAddressDto) TERS yönü; alanlar birebir). Address bir VO
    // (IEntity değil) → statik-mapper konvansiyon ağına takılmaz; mevcut ToAddress deseniyle hizalı.
    private static N11ShipmentAddressDto ToAddressDto(Address address, string? fallbackTitle = null)
    {
        return new N11ShipmentAddressDto
        {
            // N11 adres başlığı zorunlu → adresin kendi başlığı boşsa şube adından türetilen fallback'e düş.
            Title = string.IsNullOrWhiteSpace(address.Title) ? fallbackTitle : address.Title,
            City = address.City,
            Line = address.Line,
            District = address.District,
            Neighborhood = address.Neighborhood,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode,
            CityCode = address.CityCode,
            DistrictCode = address.DistrictCode,
            AdministrativeAreaId = address.AdministrativeAreaId,
            LocalityId = address.LocalityId,
            AdministrativeAreaIsoCode = address.AdministrativeAreaIsoCode,
            BuildingName = address.BuildingName,
            BuildingNumber = address.BuildingNumber,
            Room = address.Room,
            Floor = address.Floor,
            Postbox = address.Postbox,
            AdditionalStreetName = address.AdditionalStreetName,
        };
    }

    private static Address ToAddress(N11ShipmentAddressData data)
    {
        return new Address(
            city: data.CityName,
            line: data.Line,
            district: data.DistrictName,
            neighborhood: null,
            postalCode: data.PostalCode,
            countryCode: "TR",
            title: data.Title,
            cityCode: string.IsNullOrWhiteSpace(data.CityCode) ? null : data.CityCode,
            districtCode: data.DistrictId);
    }

    // ── Host-global referans çözüm sözlükleri (host'a sabitlenmiş okuma) ─────────────────────────────

    private async Task<Dictionary<string, N11ShipmentCompanyRef>> LoadCompanyRefsByExternalIdAsync()
    {
        using (CurrentTenant.Change(null))
        {
            var list = await _shipmentCompanyRepository.GetListAsync();
            return list.ToDictionary(
                x => x.ExternalId,
                x => new N11ShipmentCompanyRef(x.Name, x.ShortName),
                StringComparer.Ordinal);
        }
    }

    private async Task<Dictionary<string, string>> LoadExternalIdByShortNameAsync()
    {
        using (CurrentTenant.Change(null))
        {
            var list = await _shipmentCompanyRepository.GetListAsync();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in list)
            {
                // ShortName OPSİYONEL (2026-07-26): N11 kısa-kodsuz firma döndürebiliyor (canlıda 3 satır NULL).
                // Sözlük ANAHTARI olduğundan null atlanmalı — aksi halde ArgumentNullException tüm şablon
                // import'unu düşürürdü. Kodsuz firma zaten ShortName ile geri-çözülemez (eşleşme kaynağı yok).
                if (string.IsNullOrEmpty(company.ShortName))
                {
                    continue;
                }

                dict.TryAdd(company.ShortName, company.ExternalId);
            }

            return dict;
        }
    }

    private async Task<Dictionary<string, string>> LoadCityNamesByCodeAsync()
    {
        using (CurrentTenant.Change(null))
        {
            var list = await _cityRepository.GetListAsync();
            return list.ToDictionary(x => x.CityCode, x => x.Name, StringComparer.Ordinal);
        }
    }

    // ── Güvenlik + benzersizlik + normalize ─────────────────────────────────────────────────────────

    /// <summary>Kanalın çalışılan şirkete ait olduğunu doğrular (company query-filter yok → elle) + kimlik erişimi.</summary>
    private async Task<SalesChannelTrN11> GetOwnedChannelAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var channel = await AsyncExecuter.FirstOrDefaultAsync(
            (await _channelRepository.GetQueryableAsync()).Where(x => x.Id == salesChannelId && x.CompanyId == companyId));
        if (channel is null)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ChannelNotFound");
        }

        return channel;
    }

    /// <summary>Şablonu çalışılan şirket kapsamında yükler (yabancı şirketinki → dostane bulunamadı).</summary>
    private async Task<N11ShipmentTemplate> GetOwnedTemplateAsync(Guid id)
    {
        var companyId = EnsureCurrentCompanyId();
        var entity = await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.Id == id && x.CompanyId == companyId));
        if (entity is null)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:TemplateNotFound");
        }

        return entity;
    }

    /// <summary>Çekirdek kargo şablonunu çalışılan şirket kapsamında yükler (FORWARD taslak kaynağı; yabancı şirketinki →
    /// dostane bulunamadı). Çekirdek company query-filter yok → elle scope (GetOwnedChannel/Template deseni).</summary>
    private async Task<ShipmentTemplate> GetOwnedCoreTemplateAsync(Guid shipmentTemplateId)
    {
        var companyId = EnsureCurrentCompanyId();
        var core = await AsyncExecuter.FirstOrDefaultAsync(
            (await _coreTemplateRepository.GetQueryableAsync()).Where(x => x.Id == shipmentTemplateId && x.CompanyId == companyId));
        if (core is null)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:CoreTemplateNotFound");
        }

        return core;
    }

    /// <summary>N11 CreateOrUpdate zorunlulukları (canlı testte tek tek doğrulandı) — kullanıcıya NET Türkçe hata,
    /// N11'in kriptik "systemError"i yerine. n11.com anlaşmalı kargo (2019 mandası) zorunlu → beraberinde iade
    /// kargo firması, iade adresi ve üç bilgi metni (teslimat/değişim/taksit-vade farkı) da zorunlu.</summary>
    private static void EnsureN11Requirements(IN11ShipmentTemplateInput input)
    {
        if (!input.UseDmallCargo)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:UseDmallCargoRequired");
        }

        // Anlaşmalı kargoda kargo ödeme tipi YALNIZ mağaza öder (2) / şartlı (3) — alıcı öder (1) N11'de reddedilir (canlı doğrulandı).
        if (input.DeliveryFeeType != N11DeliveryFeeType.SellerPays && input.DeliveryFeeType != N11DeliveryFeeType.Conditional)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:DeliveryFeeTypeInvalid");
        }

        if (string.IsNullOrWhiteSpace(input.ClaimShipmentCompanyExternalId))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ClaimCompanyRequired");
        }

        if (input.ExchangeAddress is null)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ExchangeAddressRequired");
        }

        if (string.IsNullOrWhiteSpace(input.ShippingInfo))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ShippingInfoRequired");
        }

        if (string.IsNullOrWhiteSpace(input.ExchangeInfo))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:ExchangeInfoRequired");
        }

        if (string.IsNullOrWhiteSpace(input.InstallmentInfo))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:InstallmentInfoRequired");
        }

        // Depo + iade/değişim adresleri N11 adres zorunluluklarını (başlık / açık adres ≥10 / posta kodu) sağlamalı.
        EnsureN11AddressRequirements(input.WarehouseAddress);
        if (input.ExchangeAddress is not null)
        {
            EnsureN11AddressRequirements(input.ExchangeAddress);
        }
    }

    /// <summary>N11 kargo adresi (depo/iade) alan zorunlulukları — CreateOrUpdate'te canlı doğrulandı: başlık boş olamaz,
    /// açık adres en az <see cref="N11ShipmentConsts.AddressLineMinLength"/> karakter, posta kodu boş olamaz. N11'in kriptik
    /// "shipmentAddress.*" hatası yerine kullanıcıya önden NET Türkçe uyarı verir.</summary>
    private static void EnsureN11AddressRequirements(N11ShipmentAddressDto address)
    {
        if (string.IsNullOrWhiteSpace(address.Title))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:AddressTitleRequired");
        }

        if (string.IsNullOrWhiteSpace(address.Line) || address.Line.Trim().Length < N11ShipmentConsts.AddressLineMinLength)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:AddressLineTooShort");
        }

        if (string.IsNullOrWhiteSpace(address.PostalCode))
        {
            throw new BusinessException("TradeXpress:N11:Shipment:AddressPostalCodeRequired");
        }
    }

    /// <summary>Kanal içinde şablon adı benzersizliği ((SalesChannelId, TemplateName) unique index'iyle hizalı).</summary>
    private async Task EnsureTemplateNameUniqueAsync(Guid salesChannelId, string templateName, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.SalesChannelId == salesChannelId && x.Id != excludeId && x.TemplateName == templateName));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:TemplateNameAlreadyExists");
        }
    }

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:CompanyRequired");
        }

        return companyId;
    }

    /// <summary>Şablon adı normalizasyonu — entity guard'ıyla (EnsureRequiredText) hizalı sadeleştirme (trim).</summary>
    private static string NormalizeName(string? templateName)
    {
        return templateName?.Trim() ?? string.Empty;
    }
}
