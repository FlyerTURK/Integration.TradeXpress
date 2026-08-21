using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.N11Shipments;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels.N11;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Integration.TradeXpress.N11Products;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// N11 satış kanalı CRUD (tipe-özel) — <b>company-owned + per-tenant</b> (Product deseni). Kapsam DAİMA çalışılan
/// şirket (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId GÖNDERMEZ). TPT alt-tipi
/// <see cref="SalesChannelTrN11"/>. Kod benzersizliği company-scoped ((TenantId, CompanyId, Code) unique index'iyle
/// hizalı). AppKey/AppSecret opak sir — normalize EDİLMEZ (entity düz setter'la null/uzunluk guard'ı uygular).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelTrN11AppService : TradeXpressAppService, ISalesChannelTrN11AppService
{
    private readonly IRepository<SalesChannelTrN11, Guid> _repository;
    private readonly IRepository<SalesChannelBase, Guid> _baseRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly SalesChannelSubAccountBinder _subAccountBinder;
    private readonly IN11CredentialVerifier _credentialVerifier;
    private readonly IN11ShipmentTemplateAppService _shipmentTemplateAppService;

    /// <summary>Uç adresleri — yalnız "gerçek N11'e mi gidiyoruz" rozetini üretmek için okunur.</summary>
    private readonly IOptions<N11EndpointOptions> _endpointOptions;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public SalesChannelTrN11AppService(
        IRepository<SalesChannelTrN11, Guid> repository,
        IRepository<SalesChannelBase, Guid> baseRepository,
        ICurrentCompany currentCompany,
        SalesChannelSubAccountBinder subAccountBinder,
        IN11CredentialVerifier credentialVerifier,
        IN11ShipmentTemplateAppService shipmentTemplateAppService,
        IOptions<N11EndpointOptions> endpointOptions)
    {
        _endpointOptions = endpointOptions;
        _repository = repository;
        _baseRepository = baseRepository;
        _currentCompany = currentCompany;
        _subAccountBinder = subAccountBinder;
        _credentialVerifier = credentialVerifier;
        _shipmentTemplateAppService = shipmentTemplateAppService;
    }

    public virtual async Task<PagedResultDto<SalesChannelListDto>> GetListAsync(SalesChannelListRequestDto input)
    {
        // Company-owned: working şirket yoksa boş (Product deseni). Görünürlük zaten company query-filter'la da kapalı.
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<SalesChannelListDto>(0, new List<SalesChannelListDto>());
        }

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.ApplyPaging(input));

        return new PagedResultDto<SalesChannelListDto>(
            totalCount,
            items.Select(e => ObjectMapper.Map<SalesChannelTrN11, SalesChannelListDto>(e)).ToList());
    }

    public virtual async Task<SalesChannelTrN11GetDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetOwnedAsync(_currentCompany, id);
        return Redact(ObjectMapper.Map<SalesChannelTrN11, SalesChannelTrN11GetDto>(entity));
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelTrN11GetDto> CreateAsync(SalesChannelTrN11CreateDto input)
    {
        var companyId = EnsureCurrentCompanyId();

        // Tekillik kuralı: şirkette bu türden (N11) zaten bir kanal varsa ikincisi eklenemez.
        await EnsureTypeNotExistsAsync(companyId);

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu kanal → dostane hata.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(SalesChannelBase.Code), EntityFieldConsts.CodeMinLength, SalesChannelConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        // Kimlik oluşturmada ZORUNLU → N11'e doğrula; geçmezse (InvalidCredentials/VerificationUnavailable) kayıt açılmaz.
        await _credentialVerifier.VerifyOrThrowAsync(input.AppKey, input.AppSecret);

        var entity = new SalesChannelTrN11(companyId, input.Code, input.Name, input.AppKey, input.AppSecret);
        entity.SetDescription(input.Description);
        entity.SetDefaultInfos(input.DefaultShippingInfo, input.DefaultExchangeInfo, input.DefaultInstallmentInfo);
        await _subAccountBinder.BindAsync(entity, input.SubAccountId);
        await _repository.InsertAsync(entity, autoSave: true);

        // Kanal oluşturulur oluşturulmaz N11'deki mevcut kargo şablonlarını kanalın KENDİ kimliğiyle otomatik çek.
        await TrySyncShipmentTemplatesAsync(entity.Id);

        return Redact(ObjectMapper.Map<SalesChannelTrN11, SalesChannelTrN11GetDto>(entity));
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrN11GetDto> UpdateAsync(Guid id, SalesChannelTrN11UpdateDto input)
    {
        // Güvenlik sınırı (Product/Account deseni): kaydı ÖNCE yükle — company query-filter yabancı şirketinkini gizler.
        var entity = await _repository.GetOwnedAsync(_currentCompany, id);

        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        await ApplyCredentialChangeAsync(entity, input.AppKey, input.AppSecret);
        entity.SetDefaultInfos(input.DefaultShippingInfo, input.DefaultExchangeInfo, input.DefaultInstallmentInfo);
        entity.SetActive(input.IsActive);
        await _subAccountBinder.BindAsync(entity, input.SubAccountId);
        await _repository.UpdateAsync(entity, autoSave: true);

        return Redact(ObjectMapper.Map<SalesChannelTrN11, SalesChannelTrN11GetDto>(entity));
    }

    /// <summary>Sızıntısız edit kuralı: anahtar alanları BOŞ = mevcut korunur (dokunma); DOLU = N11'e doğrula, geçerse
    /// değiştir. Tek alan doldurulmuşsa (yarım kimlik) doğrulama yapılamaz → dostane hata.</summary>
    private async Task ApplyCredentialChangeAsync(SalesChannelTrN11 entity, string appKey, string appSecret)
    {
        var hasAppKey = !string.IsNullOrWhiteSpace(appKey);
        var hasAppSecret = !string.IsNullOrWhiteSpace(appSecret);
        if (!hasAppKey && !hasAppSecret)
        {
            return;   // boş bırakıldı → mevcut anahtar korunur
        }

        if (!hasAppKey || !hasAppSecret)
        {
            throw new BusinessException("TradeXpress:SalesChannel:N11:CredentialPairRequired");
        }

        await _credentialVerifier.VerifyOrThrowAsync(appKey, appSecret);
        entity.SetAppKey(appKey);
        entity.SetAppSecret(appSecret);
    }

    /// <summary>Sızıntı önleme: sir alanları client'a ASLA gitmez — GetDto her zaman boş kimlikle döner.</summary>
    /// <summary>Sırları siler + endpoint adresinin YÖNÜNÜ işaretler. İkisi de "kullanıcıya ne gösteriliyor"
    /// sorusunun parçası: sır SIZMAMALI, sahte sunucu ise GİZLENMEMELİ.</summary>
    private SalesChannelTrN11GetDto Redact(SalesChannelTrN11GetDto dto)
    {
        dto.AppKey = string.Empty;
        dto.AppSecret = string.Empty;
        dto.IsMockEndpoint = _endpointOptions.Value.IsRedirected;
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetOwnedAsync(_currentCompany, id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): normalize → değiştiyse AYNI ŞİRKET altında benzersizlik → uygula.</summary>
    private async Task ApplyCodeChangeAsync(SalesChannelTrN11 entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(SalesChannelBase.Code), EntityFieldConsts.CodeMinLength, SalesChannelConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return;
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Tekillik kuralı: şirkette bu türden (N11) kanal zaten varsa → dostane hata (her türden en fazla bir tane).
    /// IsActive'e bakılmaz (pasif de olsa tür işgal edilmiş sayılır).</summary>
    private async Task EnsureTypeNotExistsAsync(Guid companyId)
    {
        var exists = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.CompanyId == companyId));
        if (exists)
        {
            throw new BusinessException("TradeXpress:SalesChannel:TypeAlreadyExists");
        }
    }

    /// <summary>Company-scoped Code benzersizliği ((TenantId, CompanyId, Code) unique index'iyle hizalı) — TÜM alt-tipler
    /// tek base tablosunda tutulduğundan base repository üzerinden bakılır (N11 + Trendyol aynı kod kullanamaz).</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _baseRepository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:SalesChannel:CodeAlreadyExists");
        }
    }

    /// <summary>Kanal oluşturulunca N11 kargo şablonlarını otomatik hizala — BEST-EFFORT: N11 erişilemezse/başarısızsa
    /// kanal oluşturma ETKİLENMEZ (yalnız uyarı loglanır; kullanıcı sonra drill'deki "N11 ile Hizala" ile elle tetikler).
    /// Kimlik doğrulaması create'te zaten yapıldı → creds geçerli; şablon çekimi kanalın stored kimliğiyledir.</summary>
    private async Task TrySyncShipmentTemplatesAsync(Guid salesChannelId)
    {
        try
        {
            await _shipmentTemplateAppService.SyncAsync(salesChannelId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "N11 kanalı {ChannelId} oluşturuldu ama kargo şablonları otomatik içe aktarılamadı (best-effort).", salesChannelId);
        }
    }

    /// <summary>Sızıntı önleme (Product/Account deseni, fail-closed): aktif şirket working-context'ten zorlanır; yoksa fail-closed.</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:SalesChannel:CompanyRequired");
        }

        return companyId;
    }
}
