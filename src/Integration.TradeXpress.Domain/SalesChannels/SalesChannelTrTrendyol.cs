namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Trendyol (Türkiye pazaryeri) satış kanalı — <see cref="SalesChannelBase"/>'in somut TPT alt-tipi. Kendi tablosu
/// <c>AppSalesChannelTrTrendyol</c>; taban kimlik alanları <c>AppSalesChannels</c>'ta (paylaşılan PK/FK).
/// Adlandırma deseni: SalesChannel{Ülke}{Pazaryeri} (Tr + Trendyol) — <see cref="SalesChannelTrN11"/> ile hizalı.
///
/// <para>Trendyol API kimlik bilgileri (OPAK SIR; N11'den FARKLI adlandırma): <see cref="SellerId"/> (Satıcı ID)
/// + <see cref="ApiKey"/> + <see cref="ApiSecret"/>. Hiçbiri normalize EDİLMEZ (uppercase/trim credential'ı bozar);
/// <see cref="SellerId"/> matematiksel değil bir KİMLİK olduğundan string'tir (sayı gibi görünse de). Yalnız
/// null/uzunluk guard'ı. TODO (hardening): <see cref="ApiSecret"/> at-rest ŞİFRELENMELİ (şu an düz kolon).</para>
/// </summary>
public class SalesChannelTrTrendyol : SalesChannelBase
{
    #region Constructors

    protected SalesChannelTrTrendyol()
    {
    }

    public SalesChannelTrTrendyol(
        Guid companyId,
        string code,
        string name,
        string sellerId,
        string apiKey,
        string apiSecret,
        bool isActive = true)
        : base(companyId, code, name, isActive)
    {
        SetSellerId(sellerId);
        SetApiKey(apiKey);
        SetApiSecret(apiSecret);
    }

    #endregion

    #region Properties

    /// <summary>Trendyol Satıcı ID'si (Seller ID) — kimlik; matematiksel değil → string (opak, normalize edilmez).</summary>
    public virtual string SellerId { get; protected set; } = null!;

    /// <summary>Trendyol API anahtarı (opak sir; normalize edilmez).</summary>
    public virtual string ApiKey { get; protected set; } = null!;

    /// <summary>Trendyol API gizli anahtarı (opak sir; normalize edilmez). TODO: at-rest şifrelenmeli (hardening).</summary>
    public virtual string ApiSecret { get; protected set; } = null!;

    /// <summary>
    /// Bu kanalın VARSAYILAN kargo firması (<c>TrendyolCargoProvider.Id</c>) — kanal ürünleri kendi kargo
    /// firması seçilmediğinde bunu devralır.
    ///
    /// <para><b>Neden KANALDA duruyor:</b> kargo şablonu ürünün değil KANALIN özelliğidir (CLAUDE.md kargo
    /// kararı) — aynı ürün her pazaryerinde farklı firmayla gider. Ürün başına tekrar tekrar seçtirmek,
    /// yüzlerce kayıtta aynı cevabı istemek olurdu.</para>
    ///
    /// <para><b>Neden id-only ve neden Guid:</b> <c>TrendyolCargoProvider</c> ayrı bir aggregate root'tur →
    /// navigasyon değil kimlik tutulur (NavigationConventionTests). Trendyol'un sayısal
    /// <c>cargoCompanyId</c>'si DEĞİL bizim kaydımızın kimliği saklanır: firma listesi bizde yaşayan bir
    /// referanstır, sayı gönderim anında o kayıttan okunur.</para>
    ///
    /// <para><b>null = seçilmedi</b> (fail-open değil): kanal ürünü de kargosuz kalır ve eksiklik ürün
    /// formunda görünür — uydurma bir firma yazmaktansa boş bırakmak dürüsttür.</para>
    /// </summary>
    public virtual Guid? DefaultCargoProviderId { get; protected set; }

    #endregion

    #region Methods

    // Credential/kimlik: normalize YOK (case/içerik korunur), yalnız null/uzunluk guard (EnsureRequiredText → tipli exception).
    public virtual void SetSellerId(string sellerId)
    {
        SellerId = StringFieldGuard.EnsureRequiredText(
            sellerId,
            nameof(SellerId),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    public virtual void SetApiKey(string apiKey)
    {
        ApiKey = StringFieldGuard.EnsureRequiredText(
            apiKey,
            nameof(ApiKey),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    public virtual void SetApiSecret(string apiSecret)
    {
        ApiSecret = StringFieldGuard.EnsureRequiredText(
            apiSecret,
            nameof(ApiSecret),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    /// <summary>Varsayılan kargo firmasını atar. <c>null</c> = seçim kaldırıldı (meşru); doğrulama burada
    /// YAPILMAZ — kaydın gerçekten var olduğu app service katmanında sınanır (domain, host-global bir
    /// tabloya sorgu atamaz).</summary>
    public virtual void SetDefaultCargoProvider(Guid? cargoProviderId)
    {
        DefaultCargoProviderId = cargoProviderId == Guid.Empty ? null : cargoProviderId;
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
