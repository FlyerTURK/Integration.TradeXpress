using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.SalesChannels.Trendyol;
using Integration.TradeXpress.TrendyolCategories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
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
    private readonly ITrendyolCredentialVerifier _credentialVerifier;
    private readonly ITrendyolCategoryAppService _categoryAppService;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public SalesChannelTrTrendyolAppService(
        IRepository<SalesChannelTrTrendyol, Guid> repository,
        IRepository<SalesChannelBase, Guid> baseRepository,
        ICurrentCompany currentCompany,
        ITrendyolCredentialVerifier credentialVerifier,
        ITrendyolCategoryAppService categoryAppService)
    {
        _repository = repository;
        _baseRepository = baseRepository;
        _currentCompany = currentCompany;
        _credentialVerifier = credentialVerifier;
        _categoryAppService = categoryAppService;
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

        // Token yapıştırıldıysa apiKey:apiSecret'a ayır (elle yazımdaki I/l/1/O/0 karışıklığını atlatır); yoksa ayrı alanlar.
        var effectiveApiKey = input.ApiKey;
        var effectiveApiSecret = input.ApiSecret;
        if (TryDecodeToken(input.Token, out var tokenApiKey, out var tokenApiSecret))
        {
            effectiveApiKey = tokenApiKey;
            effectiveApiSecret = tokenApiSecret;
        }

        // Kimlik oluşturmada ZORUNLU → Trendyol'a doğrula (hafif authenticated GET; SellerId path'te olduğundan
        // hem kimlik hem SellerId sınanır). Geçmezse (InvalidCredentials/VerificationUnavailable) kayıt açılmaz.
        await _credentialVerifier.VerifyOrThrowAsync(input.SellerId, effectiveApiKey, effectiveApiSecret);

        var entity = new SalesChannelTrTrendyol(companyId, input.Code, input.Name, input.SellerId, effectiveApiKey, effectiveApiSecret);
        entity.SetDescription(input.Description);
        entity.SetSideCosts(SideCostSettingsFactory.Build(input.SideCosts));
        await _repository.InsertAsync(entity, autoSave: true);

        // Kanal oluşturulur oluşturulmaz Trendyol kategori ağacını (host-global) otomatik senkronize et — kimlik create'te
        // zaten doğrulandı. Kategori picker'ının ilk açılışta dolu gelmesi için (N11 kargo şablonu otomatik-import deseni).
        await TrySyncCategoriesAsync();

        return Redact(ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>(entity));
    }

    /// <summary>Kanal oluşturulunca Trendyol kategori ağacını otomatik senkronize et — BEST-EFFORT: Trendyol erişilemezse/
    /// başarısızsa kanal oluşturma ETKİLENMEZ (yalnız uyarı loglanır; kullanıcı sonra kanal formundaki "Kategorileri
    /// Senkronize Et" ile elle tetikler). Kimlik create'te zaten doğrulandı; sync kanalın stored kimliğiyle çözülür.</summary>
    private async Task TrySyncCategoriesAsync()
    {
        try
        {
            await _categoryAppService.SyncCategoriesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Trendyol kanalı oluşturuldu ama kategori ağacı otomatik senkronize edilemedi (best-effort).");
        }
    }

    [Authorize(TradeXpressPermissions.SalesChannels.Update)]
    public virtual async Task<SalesChannelTrTrendyolGetDto> UpdateAsync(Guid id, SalesChannelTrTrendyolUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);

        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        await ApplyCredentialChangeAsync(entity, input.SellerId, input.ApiKey, input.ApiSecret, input.Token);
        entity.SetSideCosts(SideCostSettingsFactory.Build(input.SideCosts));
        entity.SetActive(input.IsActive);
        await _repository.UpdateAsync(entity, autoSave: true);

        return Redact(ObjectMapper.Map<SalesChannelTrTrendyol, SalesChannelTrTrendyolGetDto>(entity));
    }

    /// <summary>Sızıntısız edit kuralı: ApiKey/ApiSecret BOŞ = mevcut korunur; DOLU = değiştir. Tek alan doldurulmuşsa
    /// (yarım kimlik) → dostane hata. Kimlik (SellerId ya da key/secret) DEĞİŞİYORSA efektif üçlüyü Trendyol'a doğrula —
    /// SellerId path'te olduğundan yalnız SellerId değişse de (key/secret korunsa) doğrulama gerekir.
    /// Token yapıştırıldıysa key/secret çiftinin ALTERNATİF/öncelikli giriş yolu → decode edip apiKey/apiSecret'ı override eder.</summary>
    private async Task ApplyCredentialChangeAsync(
        SalesChannelTrTrendyol entity, string sellerId, string apiKey, string apiSecret, string token)
    {
        // Token doluysa apiKey:apiSecret'a böl → "dolu key/secret çifti" gibi ele alınır (decode her ikisini birlikte üretir).
        if (TryDecodeToken(token, out var tokenApiKey, out var tokenApiSecret))
        {
            apiKey = tokenApiKey;
            apiSecret = tokenApiSecret;
        }

        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasApiSecret = !string.IsNullOrWhiteSpace(apiSecret);
        if (hasApiKey != hasApiSecret)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Trendyol:CredentialPairRequired");
        }

        // Efektif kimlik: yeni girildiyse yeni, yoksa mevcut (sızıntısız — DOLU değilse korunur).
        var effectiveApiKey = hasApiKey ? apiKey : entity.ApiKey;
        var effectiveApiSecret = hasApiSecret ? apiSecret : entity.ApiSecret;
        var sellerIdChanged = !string.Equals(sellerId, entity.SellerId, StringComparison.Ordinal);

        if (sellerIdChanged || hasApiKey)
        {
            await _credentialVerifier.VerifyOrThrowAsync(sellerId, effectiveApiKey, effectiveApiSecret);
        }

        entity.SetSellerId(sellerId);   // SellerId sır değil → görünür/daima güncellenir
        if (hasApiKey)
        {
            entity.SetApiKey(apiKey);
            entity.SetApiSecret(apiSecret);
        }
    }

    /// <summary>Sızıntı önleme: sir alanları (ApiKey/ApiSecret) ve sır türevi Token client'a ASLA gitmez (yalnız-yazılır giriş
    /// alanı). SellerId kimliktir → görünür kalır.</summary>
    private static SalesChannelTrTrendyolGetDto Redact(SalesChannelTrTrendyolGetDto dto)
    {
        dto.ApiKey = string.Empty;
        dto.ApiSecret = string.Empty;
        dto.Token = string.Empty;
        return dto;
    }

    /// <summary>Trendyol "yapıştır" Token'ı = base64(apiKey:apiSecret) (Authorization: Basic değeri). Boşsa <c>false</c>
    /// (Token kullanılmıyor → çağıran ayrı ApiKey/ApiSecret alanlarına düşer). Geçerli base64 → UTF8 çöz, İLK ':' ile böl;
    /// apiKey boş (idx&lt;=0) ya da apiSecret boş (':' son karakter) → geçersiz. Geçersiz base64/biçim → dostane InvalidToken.</summary>
    private static bool TryDecodeToken(string token, out string apiKey, out string apiSecret)
    {
        apiKey = string.Empty;
        apiSecret = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token.Trim()));
        }
        catch (FormatException)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Trendyol:InvalidToken");
        }

        var idx = decoded.IndexOf(':');
        if (idx <= 0 || idx >= decoded.Length - 1)
        {
            throw new BusinessException("TradeXpress:SalesChannel:Trendyol:InvalidToken");
        }

        apiKey = decoded.Substring(0, idx);
        apiSecret = decoded.Substring(idx + 1);   // secret'ta ':' olabilir → yalnız ilk ':' ile bölünür
        return true;
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
