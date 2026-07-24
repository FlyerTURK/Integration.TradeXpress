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

    /// <summary>Varsayılan teslimat bilgisi metni (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur
    /// (<see cref="N11ShipmentTemplate.ShippingInfo"/>). Serbest metin; normalize EDİLMEZ (yalnız uzunluk guard'ı).</summary>
    public virtual string? DefaultShippingInfo { get; protected set; }

    /// <summary>Varsayılan iade/değişim bilgisi metni (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur
    /// (<see cref="N11ShipmentTemplate.ExchangeInfo"/>). Serbest metin; normalize EDİLMEZ.</summary>
    public virtual string? DefaultExchangeInfo { get; protected set; }

    /// <summary>Varsayılan taksit/vade farkı bilgisi metni (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur
    /// (<see cref="N11ShipmentTemplate.InstallmentInfo"/>). Serbest metin; normalize EDİLMEZ.</summary>
    public virtual string? DefaultInstallmentInfo { get; protected set; }

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

    /// <summary>Kanal düzeyi varsayılan bilgi metinlerini ata — yeni N11 kargo şablonu formunu ön-doldurmak için.
    /// Serbest metin: normalize YOK (case/içerik korunur), yalnız uzunluk guard'ı (boş → null).</summary>
    public virtual void SetDefaultInfos(string? shipping, string? exchange, string? installment)
    {
        DefaultShippingInfo = StringFieldGuard.EnsureOptionalText(
            shipping,
            nameof(DefaultShippingInfo),
            minLength: 1,
            N11ShipmentConsts.InfoMaxLength);

        DefaultExchangeInfo = StringFieldGuard.EnsureOptionalText(
            exchange,
            nameof(DefaultExchangeInfo),
            minLength: 1,
            N11ShipmentConsts.InfoMaxLength);

        DefaultInstallmentInfo = StringFieldGuard.EnsureOptionalText(
            installment,
            nameof(DefaultInstallmentInfo),
            minLength: 1,
            N11ShipmentConsts.InfoMaxLength);
    }

    public override string ToString()
    {
        return Code;
    }

    #endregion
}
