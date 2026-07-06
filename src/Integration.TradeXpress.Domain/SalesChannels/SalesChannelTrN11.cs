namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// N11 (Türkiye pazaryeri) satış kanalı — <see cref="SalesChannelBase"/>'in somut TPT alt-tipi. Kendi tablosu
/// <c>AppSalesChannelTrN11</c>; taban kimlik alanları <c>AppSalesChannels</c>'ta (paylaşılan PK/FK).
/// Adlandırma deseni: SalesChannel{Ülke}{Pazaryeri} (Tr + N11).
///
/// <para><see cref="AppKey"/>/<see cref="AppSecret"/> = N11 API kimlik bilgisi (OPAK SIR): normalize
/// EDİLMEZ (uppercase/trim/TitleCase credential'ı bozar) — yalnız null/uzunluk guard'ı uygulanır.
/// TODO (hardening): <see cref="AppSecret"/> at-rest ŞİFRELENMELİ (şu an düz kolon; sonraki güvenlik adımı).</para>
/// </summary>
public class SalesChannelTrN11 : SalesChannelBase
{
    #region Constructors

    protected SalesChannelTrN11()
    {
    }

    public SalesChannelTrN11(
        Guid companyId,
        string code,
        string name,
        string appKey,
        string appSecret,
        bool isActive = true)
        : base(companyId, code, name, isActive)
    {
        SetAppKey(appKey);
        SetAppSecret(appSecret);
    }

    #endregion

    #region Properties

    /// <summary>N11 API anahtarı (opak sir; normalize edilmez).</summary>
    public virtual string AppKey { get; protected set; } = null!;

    /// <summary>N11 API gizli anahtarı (opak sir; normalize edilmez). TODO: at-rest şifrelenmeli (hardening).</summary>
    public virtual string AppSecret { get; protected set; } = null!;

    #endregion

    #region Methods

    // Credential: normalize YOK (case/içerik korunur), yalnız null/uzunluk guard (EnsureRequiredText → tipli exception).
    public virtual void SetAppKey(string appKey)
    {
        AppKey = StringFieldGuard.EnsureRequiredText(
            appKey,
            nameof(AppKey),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    public virtual void SetAppSecret(string appSecret)
    {
        AppSecret = StringFieldGuard.EnsureRequiredText(
            appSecret,
            nameof(AppSecret),
            minLength: 1,
            SalesChannelConsts.ConfigMaxLength);
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
