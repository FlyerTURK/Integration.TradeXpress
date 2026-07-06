using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Trendyol satış kanalı CRUD (tipe-özel) — <b>company-owned + per-tenant</b> (Product deseni; N11 servisiyle simetrik).
/// TPT alt-tipi <see cref="SalesChannelTrTrendyol"/>. Kod benzersizliği company-scoped ve TÜM alt-tipleri kapsar
/// (base tablosu). SellerId/ApiKey/ApiSecret opak kimlik/sir — normalize EDİLMEZ (entity düz setter'la guard uygular).
/// </summary>
[Authorize(TradeXpressPermissions.SalesChannels.Default)]
public class SalesChannelTrTrendyolAppService : TradeXpressAppService, ISalesChannelTrTrendyolAppService
{
    private readonly IRepository<SalesChannelTrTrendyol, Guid> _repository;
    private readonly IRepository<SalesChannelBase, Guid> _baseRepository;
    private readonly ICurrentCompany _currentCompany;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public SalesChannelTrTrendyolAppService(
        IRepository<SalesChannelTrTrendyol, Guid> repository,
        IRepository<SalesChannelBase, Guid> baseRepository,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _baseRepository = baseRepository;
        _currentCompany = currentCompany;
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
            items.Select(e => ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelListDto>(e)).ToList());
    }

    public virtual async Task<SalesChannelTrTrendyolGetDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        return Redact(ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>(entity));
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Create)]
    public virtual async Task<SalesChannelTrTrendyolGetDto> CreateAsync(SalesChannelTrTrendyolCreateDto input)
    {
        var companyId = EnsureCurrentCompanyId();

        // Tekillik kuralı: şirkette bu türden (Trendyol) zaten bir kanal varsa ikincisi eklenemez.
        await EnsureTypeNotExistsAsync(companyId);

        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(SalesChannelBase.Code), EntityFieldConsts.CodeMinLength, SalesChannelConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        // Trendyol'da test API'si YOK → kimlik doğrulaması yapılmaz (yalnız N11'de var).
        var entity = new SalesChannelTrTrendyol(companyId, input.Code, input.Name, input.SellerId, input.ApiKey, input.ApiSecret);
        entity.SetDescription(input.Description);
        await _repository.InsertAsync(entity, autoSave: true);

        return Redact(ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>(entity));
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolGetDto> UpdateAsync(Guid id, SalesChannelTrTrendyolUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetSellerId(input.SellerId);   // SellerId sır değil → görünür/daima güncellenir
        ApplyKeyChange(entity, input.ApiKey, input.ApiSecret);
        entity.SetActive(input.IsActive);
        await _repository.UpdateAsync(entity, autoSave: true);

        return Redact(ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>(entity));
    }

    /// <summary>Sızıntısız edit kuralı: ApiKey/ApiSecret BOŞ = mevcut korunur; DOLU = değiştir (Trendyol'da test API'si
    /// yok → doğrulama yapılmaz). Tek alan doldurulmuşsa (yarım kimlik) → dostane hata.</summary>
    private static void ApplyKeyChange(SalesChannelTrTrendyol entity, string apiKey, string apiSecret)
    {
        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasApiSecret = !string.IsNullOrWhiteSpace(apiSecret);
        if (!hasApiKey && !hasApiSecret)
        {
            return;   // boş bırakıldı → mevcut anahtar korunur
        }

        if (!hasApiKey || !hasApiSecret)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Trendyol:CredentialPairRequired");
        }

        entity.SetApiKey(apiKey);
        entity.SetApiSecret(apiSecret);
    }

    /// <summary>Sızıntı önleme: sir alanları (ApiKey/ApiSecret) client'a ASLA gitmez. SellerId kimliktir → görünür kalır.</summary>
    private static SalesChannelTrTrendyolGetDto Redact(SalesChannelTrTrendyolGetDto dto)
    {
        dto.ApiKey = string.Empty;
        dto.ApiSecret = string.Empty;
        return dto;
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    private async Task ApplyCodeChangeAsync(SalesChannelTrTrendyol entity, string rawCode)
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

    /// <summary>Tekillik kuralı: şirkette bu türden (Trendyol) kanal zaten varsa → dostane hata (her türden en fazla bir tane).
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

    /// <summary>Company-scoped Code benzersizliği — TÜM alt-tipler tek base tablosunda (N11 + Trendyol aynı kodu paylaşamaz).</summary>
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
