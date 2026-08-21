using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.SalesChannels.Etsy;

/// <summary>PKCE state cache kaydı — OAuth'u başlatan bağlamı (kanal + tenant + ŞİRKET) callback'e
/// taşıyan kayıt. Key = state (CSRF nonce); değer callback'te kanalı çözmeye yeter. Tek kullanımlık (okununca silinir).</summary>
public class EtsyOAuthStateCacheItem
{
    public Guid ChannelId { get; set; }
    public Guid? TenantId { get; set; }

    /// <summary>Kanalın sahibi şirket — callback isteğinde working context YOKTUR ve şirket bağlamı
    /// kurulmazsa kanal <see cref="ICompanyOwned"/> filtresi altında GÖRÜNMEZ (sentinel) ya da daha kötüsü,
    /// bağlam null kalırsa filtre permissive kola düşüp YABANCI şirketin kanalı da erişilebilir olur.
    /// Doğru cevap ikisi de değil: akışı başlatan şirket burada taşınır ve callback onunla koşar.</summary>
    public Guid CompanyId { get; set; }

    public string CodeVerifier { get; set; } = string.Empty;
}

/// <summary>
/// <see cref="IEtsyOAuthService"/> implementasyonu. Başlatma: kriptografik state + code_verifier (S256 challenge) üret,
/// <see cref="IDistributedCache{TCacheItem}"/>'e koy (10 dk TTL), authorize URL döndür. Callback: state'i tek-kullanımlık
/// doğrula (CSRF), tenant VE ŞİRKET bağlamını cache kaydından geri yükle (callback isteğinde kimlik → working context
/// YOK; şirket açıkça kurulmazsa kanal ya sentinel altında görünmez ya da bağlam null kalıp filtre permissive kola
/// düşer — "permissive olduğundan kanal görünür" eski notu güvenlik açığını davranış sanıyordu), token değişimini yap
/// ve kanala ATOMİK yaz. Redirect URI <c>App:SelfUrl</c>'den türetilir (Etsy app kaydıyla birebir aynı olmalı).
/// </summary>
public class EtsyOAuthService : IEtsyOAuthService, ITransientDependency
{
    private readonly IRepository<SalesChannelEtsy, Guid> _repository;
    private readonly IEtsyOAuthClient _oauthClient;
    private readonly IDistributedCache<EtsyOAuthStateCacheItem> _stateCache;
    private readonly IConfiguration _configuration;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;
    private readonly ILogger<EtsyOAuthService> _logger;

    public EtsyOAuthService(
        IRepository<SalesChannelEtsy, Guid> repository,
        IEtsyOAuthClient oauthClient,
        IDistributedCache<EtsyOAuthStateCacheItem> stateCache,
        IConfiguration configuration,
        ICurrentTenant currentTenant,
        ICurrentCompany currentCompany,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock,
        ILogger<EtsyOAuthService> logger)
    {
        _repository = repository;
        _oauthClient = oauthClient;
        _stateCache = stateCache;
        _configuration = configuration;
        _currentTenant = currentTenant;
        _currentCompany = currentCompany;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task<string> StartAsync(SalesChannelEtsy channel)
    {
        // PKCE: verifier 32 rastgele bayt (base64url ~43 karakter — spec 43-128 aralığında); challenge = S256(verifier).
        var codeVerifier = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var codeChallenge = ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = ToBase64Url(RandomNumberGenerator.GetBytes(16));

        await _stateCache.SetAsync(
            state,
            new EtsyOAuthStateCacheItem
            {
                ChannelId = channel.Id,
                TenantId = _currentTenant.Id,
                CompanyId = channel.CompanyId,   // ambient'ten DEĞİL kanalın kendisinden — akış tek kanala bağlı
                CodeVerifier = codeVerifier,
            },
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(EtsyOAuthConsts.StateCacheMinutes),
            });

        return EtsyOAuthConsts.AuthorizeUrl
            + "?response_type=code"
            + $"&client_id={Uri.EscapeDataString(channel.Keystring)}"
            + $"&redirect_uri={Uri.EscapeDataString(BuildRedirectUri())}"
            + $"&scope={Uri.EscapeDataString(EtsyOAuthConsts.Scopes)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + $"&code_challenge={Uri.EscapeDataString(codeChallenge)}"
            + "&code_challenge_method=S256";
    }

    public virtual async Task<EtsyOAuthCallbackResult> HandleCallbackAsync(string? state, string? code, string? error)
    {
        // 1) State → cache (CSRF): bilinmeyen/expired state = akış bizden başlamamış ya da 10 dk aşılmış.
        if (string.IsNullOrWhiteSpace(state))
        {
            _logger.LogWarning("Etsy OAuth callback state olmadan çağrıldı.");
            return new EtsyOAuthCallbackResult(false, null);
        }

        var item = await _stateCache.GetAsync(state);
        if (item == null)
        {
            _logger.LogWarning("Etsy OAuth callback bilinmeyen/expired state ile çağrıldı.");
            return new EtsyOAuthCallbackResult(false, null);
        }

        await _stateCache.RemoveAsync(state);   // tek kullanımlık — replay engeli

        // 2) Satıcı onayı reddetti ya da Etsy hata döndürdü (code yok) → kanal biliniyor, dostane hata.
        if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("Etsy OAuth onayı tamamlanmadı (error={Error}).", error);
            return new EtsyOAuthCallbackResult(false, item.ChannelId);
        }

        // 3) Token değişimi + kanala atomik yazım. Tenant VE ŞİRKET cache kaydından geri yüklenir: callback
        // isteğinin kimliği yoktur, dolayısıyla working context de yoktur. Şirket bağlamı kurulmazsa kanal ya
        // sentinel altında görünmez (EntityNotFound) ya da bağlam null kalırsa filtre permissive kola düşüp
        // yabancı şirketin kanalını da erişilebilir kılar — ikisi de kabul edilemez, akışı başlatan şirketle koş.
        try
        {
            using (_currentTenant.Change(item.TenantId))
            using (_currentCompany.Change(item.CompanyId))
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                var channel = await _repository.GetAsync(item.ChannelId);

                var tokens = await _oauthClient.ExchangeAuthorizationCodeAsync(
                    channel.Keystring, code, item.CodeVerifier, BuildRedirectUri());

                ApplyTokens(channel, tokens);

                // Mağaza bilgisi best-effort (getMe/getShop) — başarısızlık bağlantıyı düşürmez.
                // x-api-key BİRLEŞİK {keystring}:{secret} (canlı teyitli Etsy gerekliliği).
                var (shopId, shopName) = await _oauthClient.TryGetShopInfoAsync(
                    $"{channel.Keystring}:{channel.SharedSecret}", tokens.AccessToken);
                if (shopId != null)
                {
                    channel.SetShopInfo(shopId, shopName);
                }

                await _repository.UpdateAsync(channel, autoSave: true);
                await uow.CompleteAsync();
            }

            return new EtsyOAuthCallbackResult(true, item.ChannelId);
        }
        catch (Exception ex)
        {
            // Endpoint fırlatmaz — kullanıcı dostane hata sayfasına yönlendirilir, teknik detay logda kalır.
            _logger.LogError(ex, "Etsy OAuth token değişimi başarısız (ChannelId={ChannelId}).", item.ChannelId);
            return new EtsyOAuthCallbackResult(false, item.ChannelId);
        }
    }

    /// <summary>Token yanıtını kanala uygular: rotasyonlu çift atomik yazılır (kayıt=UTC); EtsyUserId access token'ın
    /// "{user_id}.{token}" ön-ekinden türetilir.</summary>
    private void ApplyTokens(SalesChannelEtsy channel, EtsyTokenResult tokens)
    {
        var utcNow = _clock.Now.ToUniversalTime();
        channel.SetTokens(
            tokens.AccessToken,
            utcNow.AddSeconds(tokens.ExpiresInSeconds),
            tokens.RefreshToken,
            utcNow.AddDays(EtsyOAuthConsts.RefreshTokenLifetimeDays));

        var dotIndex = tokens.AccessToken.IndexOf('.');
        if (dotIndex > 0)
        {
            channel.SetEtsyUserId(tokens.AccessToken.Substring(0, dotIndex));
        }
    }

    /// <summary>redirect_uri = App:SelfUrl + callback path — Etsy uygulama kaydındaki URI ile BİREBİR aynı olmalı
    /// (case-sensitive, trailing-slash'siz). Kanonik: https://umut.taile7a850.ts.net:44318/etsy/oauth-callback.</summary>
    private string BuildRedirectUri()
    {
        var selfUrl = _configuration["App:SelfUrl"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(selfUrl))
        {
            // Konfig eksikliği kurulum hatasıdır (fail-fast) — sessizce yanlış URI kurup Etsy'de kafa karıştırma.
            throw new Volo.Abp.AbpException("App:SelfUrl konfigürasyonu eksik — Etsy OAuth redirect_uri kurulamıyor.");
        }

        return selfUrl + EtsyOAuthConsts.CallbackPath;
    }

    /// <summary>Base64url (RFC 7636): '+'→'-', '/'→'_', padding yok — PKCE/state değerleri URL-güvenli olmalı.</summary>
    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
