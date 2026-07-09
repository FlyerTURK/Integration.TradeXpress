namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Etsy (global platform) satış kanalı — <see cref="SalesChannelBase"/>'in somut TPT alt-tipi. Kendi tablosu
/// <c>AppSalesChannelEtsy</c>; taban kimlik alanları <c>AppSalesChannels</c>'ta (paylaşılan PK/FK).
/// Adlandırma: Etsy TEK global platformdur (ülke = shop location) → <c>Tr</c> ülke öneki YOK (N11/Trendyol'dan farklı;
/// 2026-07 araştırma kararı — bkz. .claude/research/etsy/etsy-api-overview.md §Entity adlandırma).
///
/// <para><b>Kimlik modeli N11/Trendyol'dan FARKLI:</b> statik credential yerine OAuth 2.0 Authorization Code + PKCE.
/// <see cref="Keystring"/> (public client_id — sır DEĞİL) + <see cref="SharedSecret"/> (sır) uygulama kimliğidir;
/// <see cref="AccessToken"/> (1 saat) + <see cref="RefreshToken"/> (90 gün, HER yenilemede DEĞİŞİR — rotasyon)
/// satıcı-onaylı süreli token'lardır. Token alanları MUTABLE (OAuth callback/refresh günceller) — set-once değil.
/// Hiçbiri normalize EDİLMEZ (opak). TODO (hardening): <see cref="SharedSecret"/> + token'lar at-rest ŞİFRELENMELİ
/// (Trendyol ApiSecret'taki TODO ile hizalı; şu an düz kolon).</para>
/// </summary>
public class SalesChannelEtsy : SalesChannelBase
{
    #region Constructors

    protected SalesChannelEtsy()
    {
    }

    public SalesChannelEtsy(
        Guid companyId,
        string code,
        string name,
        string keystring,
        string sharedSecret,
        bool isActive = true)
        : base(companyId, code, name, isActive)
    {
        SetKeystring(keystring);
        SetSharedSecret(sharedSecret);
    }

    #endregion

    #region Properties

    /// <summary>Etsy uygulama anahtarı (keystring) = OAuth <c>client_id</c> + her istekte <c>x-api-key</c> header'ı.
    /// Public client kimliği — SIR DEĞİL (görünür kalabilir); yine de opak, normalize edilmez.</summary>
    public virtual string Keystring { get; protected set; } = null!;

    /// <summary>Etsy uygulama gizli anahtarı (opak sir; normalize edilmez). TODO: at-rest şifrelenmeli (hardening).</summary>
    public virtual string SharedSecret { get; protected set; } = null!;

    /// <summary>Etsy mağaza ID'si — kimlik; matematiksel değil → string (OAuth bağlantısında API'den çözülür).</summary>
    public virtual string? ShopId { get; protected set; }

    /// <summary>Etsy mağaza adı — yalnız görüntü (OAuth bağlantısında API'den çözülür).</summary>
    public virtual string? ShopName { get; protected set; }

    /// <summary>Etsy kullanıcı ID'si — access token'ın "{user_id}.{token}" ön-ekinden türetilir (bazı çağrılarda gerekir).</summary>
    public virtual string? EtsyUserId { get; protected set; }

    /// <summary>OAuth access token (1 saat ömür; "{user_id}.{token}" biçimi). Null = hiç bağlanılmadı/bağlantı temizlendi.</summary>
    public virtual string? AccessToken { get; protected set; }

    /// <summary>Access token'ın UTC son-geçerlilik anı (kayıt=UTC kuralı).</summary>
    public virtual DateTime? AccessTokenExpiresAt { get; protected set; }

    /// <summary>OAuth refresh token (90 gün; HER yenilemede Etsy YENİSİNİ döner — rotasyon, eskisi saklanmaz).</summary>
    public virtual string? RefreshToken { get; protected set; }

    /// <summary>Refresh token'ın UTC son-geçerlilik anı. Geçtiyse satıcı yeniden onay vermek zorunda ("yeniden bağlan").</summary>
    public virtual DateTime? RefreshTokenExpiresAt { get; protected set; }

    #endregion

    #region Methods

    // Credential/kimlik: normalize YOK (case/içerik korunur), yalnız null/uzunluk guard (EnsureRequiredText → tipli exception).
    /// <summary>Keystring (client_id) değişirse mevcut token'lar ESKİ uygulamaya aittir → temizlenir (yeniden bağlan gerekir).</summary>
    public virtual void SetKeystring(string keystring)
    {
        var normalized = StringFieldGuard.EnsureRequiredText(
            keystring,
            nameof(Keystring),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);

        if (!string.Equals(Keystring, normalized, StringComparison.Ordinal))
        {
            ClearTokens();
        }

        Keystring = normalized;
    }

    public virtual void SetSharedSecret(string sharedSecret)
    {
        SharedSecret = StringFieldGuard.EnsureRequiredText(
            sharedSecret,
            nameof(SharedSecret),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    /// <summary>OAuth bağlantısında API'den çözülen mağaza bilgisi (best-effort — null kalabilir). Opak/dış veri:
    /// normalize edilmez (yalnız trim + uzunluk guard'ı; boş → null).</summary>
    public virtual void SetShopInfo(string? shopId, string? shopName)
    {
        ShopId = StringFieldGuard.EnsureOptionalText(shopId, nameof(ShopId), minLength: 1, SalesChannelConsts.ConfigMaxLength);
        ShopName = StringFieldGuard.EnsureOptionalText(shopName, nameof(ShopName), minLength: 1, SalesChannelConsts.NameMaxLength);
    }

    public virtual void SetEtsyUserId(string? etsyUserId)
    {
        EtsyUserId = StringFieldGuard.EnsureOptionalText(etsyUserId, nameof(EtsyUserId), minLength: 1, SalesChannelConsts.ConfigMaxLength);
    }

    /// <summary>Token çiftini TEK atomik adımda günceller (rotasyon: refresh token her yenilemede değişir — yarım
    /// güncelleme access/refresh uyumsuzluğu yaratırdı). OAuth callback'i ve otomatik refresh çağırır. Anlar UTC.</summary>
    public virtual void SetTokens(
        string accessToken,
        DateTime accessTokenExpiresAt,
        string refreshToken,
        DateTime refreshTokenExpiresAt)
    {
        AccessToken = StringFieldGuard.EnsureRequiredText(
            accessToken,
            nameof(AccessToken),
            minLength: 1,
            SalesChannelConsts.OAuthTokenMaxLength);
        AccessTokenExpiresAt = accessTokenExpiresAt;
        RefreshToken = StringFieldGuard.EnsureRequiredText(
            refreshToken,
            nameof(RefreshToken),
            minLength: 1,
            SalesChannelConsts.OAuthTokenMaxLength);
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
    }

    /// <summary>Bağlantıyı kopar (token'ları düşür) — keystring değişimi ya da elle "bağlantıyı kes".</summary>
    public virtual void ClearTokens()
    {
        AccessToken = null;
        AccessTokenExpiresAt = null;
        RefreshToken = null;
        RefreshTokenExpiresAt = null;
    }

    /// <summary>Kanal Etsy'ye bağlı mı: refresh token dolu VE süresi geçmemiş (access token süresi dolmuş olabilir —
    /// refresh ile tazelenir, bağlantıyı düşürmez). Saat çağırandan gelir (UTC) — entity clock bilmez.</summary>
    public virtual bool IsConnected(DateTime utcNow)
    {
        return !string.IsNullOrEmpty(RefreshToken)
            && RefreshTokenExpiresAt.HasValue
            && RefreshTokenExpiresAt.Value > utcNow;
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
