using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels.Etsy;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Etsy satış kanalı CRUD (tipe-özel) — <b>company-owned + per-tenant</b> (N11/Trendyol servisleriyle simetrik).
/// TPT alt-tipi <see cref="SalesChannelEtsy"/>. Kod benzersizliği company-scoped ve TÜM alt-tipleri kapsar (base tablosu).
/// Kimlik modeli FARKLI: Keystring (public client_id — görünür) + SharedSecret (sır — redakte) statik; access/refresh
/// token'ları OAuth akışı doldurur ve DTO'ya HİÇ çıkmaz (yalnız türetilmiş IsConnected). Trendyol'un Token-yapıştır
/// deseni Etsy'de YOK (OAuth redirect akışı var).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelEtsyAppService : TradeXpressAppService, ISalesChannelEtsyAppService
{
    private readonly IRepository<SalesChannelEtsy, Guid> _repository;
    private readonly IRepository<SalesChannelBase, Guid> _baseRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IEtsyCredentialVerifier _credentialVerifier;
    private readonly IEtsyOAuthService _oauthService;
    private readonly IClock _clock;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public SalesChannelEtsyAppService(
        IRepository<SalesChannelEtsy, Guid> repository,
        IRepository<SalesChannelBase, Guid> baseRepository,
        ICurrentCompany currentCompany,
        IEtsyCredentialVerifier credentialVerifier,
        IEtsyOAuthService oauthService,
        IClock clock)
    {
        _repository = repository;
        _baseRepository = baseRepository;
        _currentCompany = currentCompany;
        _credentialVerifier = credentialVerifier;
        _oauthService = oauthService;
        _clock = clock;
    }

    public virtual async Task<PagedResultDto<SalesChannelListDto>> GetListAsync(SalesChannelListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new PagedResultDto<SalesChannelListDto>(0, new List<SalesChannelListDto>());
        }

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<SalesChannelListDto>(
            totalCount,
            items.Select(e => ObjectMapper.Map<SalesChannelEtsy, SalesChannelListDto>(e)).ToList());
    }

    public virtual async Task<SalesChannelEtsyGetDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        return ToRedactedGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelEtsyGetDto> CreateAsync(SalesChannelEtsyCreateDto input)
    {
        var companyId = EnsureCurrentCompanyId();

        // Tekillik kuralı: şirkette bu türden (Etsy) zaten bir kanal varsa ikincisi eklenemez.
        await EnsureTypeNotExistsAsync(companyId);

        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(SalesChannelBase.Code), EntityFieldConsts.CodeMinLength, SalesChannelConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        // Kimlik oluşturmada ZORUNLU doğrulanır (OAuth'suz public ping — x-api-key {keystring}:{secret} BİRLEŞİK,
        // canlı teyitli: probe HEM keystring HEM SharedSecret'ı sınar). Geçmezse kayıt açılmaz.
        await _credentialVerifier.VerifyOrThrowAsync(input.Keystring, input.SharedSecret);

        var entity = new SalesChannelEtsy(companyId, input.Code, input.Name, input.Keystring, input.SharedSecret);
        entity.SetDescription(input.Description);
        await _repository.InsertAsync(entity, autoSave: true);

        return ToRedactedGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelEtsyGetDto> UpdateAsync(Guid id, SalesChannelEtsyUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        await ApplyCredentialChangeAsync(entity, input.Keystring, input.SharedSecret);
        entity.SetActive(input.IsActive);
        await _repository.UpdateAsync(entity, autoSave: true);

        return ToRedactedGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>OAuth 2.0 PKCE akışını başlat — state/verifier üretilip cache'lenir, satıcının yönlendirileceği Etsy
    /// onay URL'i döner. Token yazma yetkisiyle eşdeğer bir kanal-yapılandırma işlemi → Update izni.</summary>
    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<string> StartOAuthAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        return await _oauthService.StartAsync(entity);
    }

    /// <summary>Sızıntısız edit kuralı: SharedSecret BOŞ = mevcut korunur; DOLU = değiştir. Keystring görünür kimlik →
    /// daima güncellenir; DEĞİŞİRSE entity mevcut token'ları temizler (token'lar eski uygulamaya aittir — yeniden
    /// "Etsy'ye Bağlan" gerekir). Kimliğin herhangi bir parçası değişiyorsa EFEKTİF çift ping'le doğrulanır (probe
    /// birleşik x-api-key ile HEM keystring HEM secret'ı sınar — canlı teyitli).</summary>
    private async Task ApplyCredentialChangeAsync(SalesChannelEtsy entity, string keystring, string sharedSecret)
    {
        var keystringChanged = !string.Equals(keystring, entity.Keystring, StringComparison.Ordinal);
        var hasSecret = !string.IsNullOrWhiteSpace(sharedSecret);
        var effectiveSecret = hasSecret ? sharedSecret : entity.SharedSecret;

        if (keystringChanged || hasSecret)
        {
            await _credentialVerifier.VerifyOrThrowAsync(keystring, effectiveSecret);
        }

        entity.SetKeystring(keystring);   // değiştiyse token'ları da temizler (entity invariant'ı)

        if (hasSecret)
        {
            entity.SetSharedSecret(sharedSecret);
        }
    }

    /// <summary>Sızıntı önleme + türetilmiş durum: SharedSecret client'a ASLA gitmez (yalnız-yazılır); access/refresh
    /// token'ları DTO'da zaten yok. IsConnected sunucuda hesaplanır (refresh token dolu + süresi geçmemiş).</summary>
    private SalesChannelEtsyGetDto ToRedactedGetDto(SalesChannelEtsy entity)
    {
        var dto = ObjectMapper.Map<SalesChannelEtsy, SalesChannelEtsyGetDto>(entity);
        dto.SharedSecret = string.Empty;
        dto.IsConnected = entity.IsConnected(_clock.Now.ToUniversalTime());
        return dto;
    }

    private async Task ApplyCodeChangeAsync(SalesChannelEtsy entity, string rawCode)
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

    /// <summary>Tekillik kuralı: şirkette bu türden (Etsy) kanal zaten varsa → dostane hata (her türden en fazla bir tane).
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

    /// <summary>Company-scoped Code benzersizliği — TÜM alt-tipler tek base tablosunda (N11/Trendyol/Etsy aynı kodu paylaşamaz).</summary>
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

    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:SalesChannel:CompanyRequired");
        }

        return companyId;
    }
}
