using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Addressing;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels;
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
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<N11City, Guid> _cityRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IN11ShipmentTemplateClient _client;

    public N11ShipmentTemplateAppService(
        IRepository<N11ShipmentTemplate, Guid> repository,
        IRepository<SalesChannelTrN11, Guid> channelRepository,
        IRepository<N11ShipmentCompany, Guid> shipmentCompanyRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<N11City, Guid> cityRepository,
        ICurrentCompany currentCompany,
        IN11ShipmentTemplateClient client)
    {
        _repository = repository;
        _channelRepository = channelRepository;
        _shipmentCompanyRepository = shipmentCompanyRepository;
        _subAccountRepository = subAccountRepository;
        _cityRepository = cityRepository;
        _currentCompany = currentCompany;
        _client = client;
    }

    public virtual async Task<List<N11ShipmentTemplateDto>> GetListAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var items = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId)
                .OrderBy(x => x.TemplateName));

        var dtos = items.Select(x => ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(x)).ToList();
        await EnrichCompaniesAsync(items, dtos);
        return dtos;
    }

    public virtual async Task<N11ShipmentTemplateDto> GetAsync(Guid id)
    {
        var entity = await GetOwnedTemplateAsync(id);
        var dto = ObjectMapper.Map<N11ShipmentTemplate, N11ShipmentTemplateDto>(entity);
        await EnrichCompaniesAsync(new List<N11ShipmentTemplate> { entity }, new List<N11ShipmentTemplateDto> { dto });
        return dto;
    }

    /// <summary>Kargo firması satırlarını gösterime hazırlar: düz kimlik listesi (mevcut çoklu-seçim bileşeni için)
    /// + firma adı (host-global aynadan) + bağlı cari alt hesabın kodu. Adlar/kodlar PERSIST EDİLMEZ — tek kaynak
    /// ayna ve cari planıdır; burada yalnız okunur.</summary>
    private async Task EnrichCompaniesAsync(List<N11ShipmentTemplate> entities, List<N11ShipmentTemplateDto> dtos)
    {
        var externalIds = entities.SelectMany(e => e.Companies).Select(c => c.ExternalId).ToHashSet(StringComparer.Ordinal);
        var subAccountIds = entities
            .SelectMany(e => e.Companies)
            .Where(c => c.SubAccountId is not null)
            .Select(c => c.SubAccountId!.Value)
            .ToHashSet();

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (externalIds.Count > 0)
        {
            // Ayna HOST-GLOBAL → tenant filtresi kapatılarak okunur (şablon senkronundaki desenle aynı).
            using (CurrentTenant.Change(null))
            {
                names = (await AsyncExecuter.ToListAsync(
                        (await _shipmentCompanyRepository.GetQueryableAsync())
                            .Where(c => externalIds.Contains(c.ExternalId))
                            .Select(c => new { c.ExternalId, c.Name })))
                    .ToDictionary(x => x.ExternalId, x => x.Name, StringComparer.Ordinal);
            }
        }

        var subAccountCodes = subAccountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await AsyncExecuter.ToListAsync(
                    (await _subAccountRepository.GetQueryableAsync())
                        .Where(s => subAccountIds.Contains(s.Id))
                        .Select(s => new { s.Id, s.Code })))
                .ToDictionary(x => x.Id, x => x.Code);

        foreach (var (entity, dto) in entities.Zip(dtos))
        {
            dto.ShipmentCompanyExternalIds = entity.Companies.Select(c => c.ExternalId).ToList();
            dto.Companies = entity.Companies
                .Select(c => new N11ShipmentTemplateCompanyDto
                {
                    ExternalId = c.ExternalId,
                    Name = names.GetValueOrDefault(c.ExternalId, string.Empty),
                    SubAccountId = c.SubAccountId,
                    SubAccountCode = c.SubAccountId is { } id ? subAccountCodes.GetValueOrDefault(id) : null,
                })
                .ToList();
        }
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
                await _repository.InsertAsync(created, autoSave: true);
            }

            changed++;
        }

        // N11'de artık olmayan şablon SİLİNMEZ → PASİFLEŞTİRİLİR (2026-07-26 Hakan kararı): şablon kalkmışsa
        // onunla iş yapılmıyor demektir, ama kullanıcının kurduğu cari bağları ve geçmiş referanslar yaşamalı.
        // Şablon N11'e geri gelirse yukarıdaki güncelleme kolu onu yeniden aktifleştirir.
        var fetchedNames = new HashSet<string>(templates.Select(t => NormalizeName(t.TemplateName)), StringComparer.Ordinal);
        foreach (var gone in existing.Values.Where(e => !fetchedNames.Contains(e.TemplateName) && e.IsActive))
        {
            gone.SetActive(false);
            await _repository.UpdateAsync(gone, autoSave: true);
        }

        // Öksüz firmalar cariyi kardeş şablonlardan devralır → aynı firma ikinci kez sorulmaz.
        await InheritSubAccountsFromSiblingsAsync(channel.Id);

        return changed;
    }

    /// <summary>Bir kargo firmasının varsayılan cari alt hesabını bağlar — öksüz-sorma akışının cevabı.
    /// <para>Bağ AYNI KANALDAKİ TÜM şablonlara yayılır (firma başına tek cari = tek bakiye). <c>null</c> geçilirse
    /// bağ çözülür ve firma yeniden öksüz olur.</para>
    /// <para>Alt hesabın ŞİRKETE ait olduğu doğrulanır — company query-filter'ı zaten daraltır, ama bulunamayan
    /// id sessizce geçilmez (fail-fast): yabancı/silinmiş cariye bağ kurulamaz.</para></summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task LinkCompanySubAccountAsync(
        Guid salesChannelId, string shipmentCompanyExternalId, Guid? subAccountId)
    {
        var channel = await GetOwnedChannelAsync(salesChannelId);

        if (subAccountId is { } id && await _subAccountRepository.FindAsync(id) is null)
        {
            throw new BusinessException("TradeXpress:N11:Shipment:SubAccountNotFound");
        }

        var templates = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.SalesChannelId == channel.Id));

        foreach (var template in templates.Where(t =>
                     t.Companies.Any(c => string.Equals(c.ExternalId, shipmentCompanyExternalId, StringComparison.Ordinal))))
        {
            template.SetCompanySubAccount(shipmentCompanyExternalId, subAccountId);
            await _repository.UpdateAsync(template, autoSave: true);
        }
    }

    /// <summary>Cari alt hesabı bağlanmamış (ÖKSÜZ) kargo firmaları — firma başına TEK satır.</summary>
    public virtual async Task<List<N11ShipmentTemplateCompanyDto>> GetUnlinkedCompaniesAsync(Guid salesChannelId)
    {
        var companyId = EnsureCurrentCompanyId();
        var templates = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.SalesChannelId == salesChannelId && x.IsActive));

        var orphanIds = templates
            .SelectMany(t => t.Companies)
            .Where(c => c.SubAccountId is null)
            .Select(c => c.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (orphanIds.Count == 0)
        {
            return new List<N11ShipmentTemplateCompanyDto>();
        }

        // Ayna HOST-GLOBAL → adlar tenant filtresi kapatılarak okunur.
        Dictionary<string, string> names;
        using (CurrentTenant.Change(null))
        {
            names = (await AsyncExecuter.ToListAsync(
                    (await _shipmentCompanyRepository.GetQueryableAsync())
                        .Where(c => orphanIds.Contains(c.ExternalId))
                        .Select(c => new { c.ExternalId, c.Name })))
                .ToDictionary(x => x.ExternalId, x => x.Name, StringComparer.Ordinal);
        }

        return orphanIds
            .Select(externalId => new N11ShipmentTemplateCompanyDto
            {
                ExternalId = externalId,
                Name = names.GetValueOrDefault(externalId, string.Empty),
            })
            .OrderBy(x => x.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>ÖKSÜZ firma satırlarına (carisi boş) cariyi KARDEŞ şablonlardan devrettirir.
    /// <para>Hakan kuralı: "ilk şablonda Yurtiçi'yi sordu; ikinci şablonda yine Yurtiçi ise tekrar sormasın."
    /// Bağ şablonun içinde yaşadığı için bu devir AÇIKÇA yapılmalı — aynı firma aynı kanalda AYNI cariyi
    /// göstermeli, yoksa kargo firmasının tek bakiyesi bizde şablon şablon bölünür.</para>
    /// <para>Mevcut bağlar EZİLMEZ; yalnız boş olanlar dolar. Kullanıcı bir firmayı bilinçli olarak farklı
    /// carilere bağladıysa ilk bulunan esas alınır (çakışma bugün beklenmiyor — tek bakiye ilkesi).</para></summary>
    private async Task InheritSubAccountsFromSiblingsAsync(Guid salesChannelId)
    {
        var channelTemplates = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.SalesChannelId == salesChannelId));

        var knownSubAccounts = channelTemplates
            .SelectMany(t => t.Companies)
            .Where(c => c.SubAccountId is not null)
            .GroupBy(c => c.ExternalId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().SubAccountId!.Value, StringComparer.Ordinal);
        if (knownSubAccounts.Count == 0)
        {
            return;
        }

        foreach (var template in channelTemplates)
        {
            var filled = false;
            foreach (var company in template.Companies.Where(c => c.SubAccountId is null))
            {
                if (knownSubAccounts.TryGetValue(company.ExternalId, out var subAccountId))
                {
                    company.SetSubAccount(subAccountId);
                    filled = true;
                }
            }

            if (filled)
            {
                await _repository.UpdateAsync(template, autoSave: true);
            }
        }
    }

    // ── FORWARD taslak (çekirdek → N11 ön-doldurma) ─────────────────────────────────────────────────


    // ── REVERSE K1 köprüsü (kanal → çekirdek ters mutabakat) ────────────────────────────────────────


    // ── Uygulama (DTO/data → entity) ────────────────────────────────────────────────────────────────

    private void ApplyInput(N11ShipmentTemplate entity, IN11ShipmentTemplateInput input)
    {
        // K1 köprüsü — çekirdek şablon referansı (id-only); N11'e push EDİLMEZ, yalnız yerelde tutulur.
        entity.SetTemplateName(input.TemplateName);
        entity.SetDeliveryFeeType(input.DeliveryFeeType);
        entity.SetShipmentMethod(input.ShipmentMethod);
        // UseDmallCargo kullanıcıdan ALINMAZ — anlaşmalı kargo N11'de zorunlu (entity notu), daima true.
        entity.SetFlags(input.SpecialDelivery, input.CombinedShipmentAllowed);
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
        entity.SetFlags(data.SpecialDelivery, data.CombinedShipmentAllowed);
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
        // N11'e yalnız firma KİMLİĞİ gider; cari alt hesap bizim iç bilgimizdir (push edilmez).
        var companies = entity.Companies
            .Select(c => c.ExternalId)
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


    /// <summary>N11 CreateOrUpdate zorunlulukları (canlı testte tek tek doğrulandı) — kullanıcıya NET Türkçe hata,
    /// N11'in kriptik "systemError"i yerine. n11.com anlaşmalı kargo (2019 mandası) zorunlu → beraberinde iade
    /// kargo firması, iade adresi ve üç bilgi metni (teslimat/değişim/taksit-vade farkı) da zorunlu.</summary>
    private static void EnsureN11Requirements(IN11ShipmentTemplateInput input)
    {
        // Anlaşmalı kargo (UseDmallCargo) artık AYAR DEĞİL — daima true (entity notu), kontrol edilecek bir şey yok.
        // Ödeme tipi de enum düzeyinde 2/3'e indirildiğinden ayrıca doğrulanmaz (geçersiz değer üretilemez).

        // Teslimat ili BOŞ olamaz: N11 boş listeyi "hiçbir şehre teslimat yok" diye kaydeder — sessizce
        // işlevsiz şablon üretmemek için burada da fail-fast (entity'de de aynı guard var).
        if (input.DeliverableCityCodes is not { Count: > 0 })
        {
            throw new BusinessException("TradeXpress:N11:Shipment:DeliverableCitiesRequired");
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
