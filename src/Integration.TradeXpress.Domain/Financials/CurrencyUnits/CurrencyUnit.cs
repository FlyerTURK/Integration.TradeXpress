namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Bir fiyatlama/kayıt birimi: nakit döviz (USD, TRY) ya da maden (HAS, GUM).
/// Birim <b>kimliktir</b> — piyasa fiyatı burada DEĞİL (ExchangeRate + cache'te yaşar).
/// Burada yalnız kimlik + <b>türetme kuralı</b> (margin/follow config) tutulur.
///
/// <para>"System" kavramı saklanmaz — <c>TenantId == null</c> (host/global) birim zaten sistem
/// birimidir; DTO'da hesaplanır, koruma kuralları manager'da uygulanır.</para>
///
/// <para>Re-basing (ABD şirketinde USD=1) bu entity'de değil; aktif Company.BaseCurrencyUnitId
/// ile <see cref="CurrencyPriceCalculator.ReBase"/> tarafından yapılır.</para>
/// </summary>
public class CurrencyUnit : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected CurrencyUnit()
    {
    }

    public CurrencyUnit(
        string code,
        string name,
        CurrencyUnitType type = CurrencyUnitType.Cash,
        int displayOrder = 99,
        bool isActive = true)
    {
        SetCode(code);
        SetName(name);
        SetType(type);
        SetDisplayOrder(displayOrder);
        SetActive(isActive);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }
    public virtual string Code { get; protected set; } = null!;
    public virtual string Name { get; protected set; } = null!;
    public virtual CurrencyUnitType Type { get; protected set; }
    public virtual bool IsActive { get; protected set; }
    public virtual int DisplayOrder { get; protected set; }
    public virtual string? Description { get; protected set; }

    /// <summary>Bu birim bakiye listesinde HER ZAMAN gösterilsin mi (ör. HAS/TRY/USD/EUR). Kimlik-seviyesi
    /// (per-tenant değil); global birimlerde host belirler.</summary>
    public virtual bool AlwaysShowInBalance { get; protected set; }

    // ── Takip (follow) ilişkisi — YAPISAL/GLOBAL (feed-seviyesi türetme) ───────
    // Alış/satış marjı burada DEĞİL — o per-tenant CurrencyUnitMargin'de yaşar.
    // Following ise kimliğin parçası: "PLD, HAS'tan şu margin'le türetilir" herkes için aynı.
    // Aggregate sınırı: parent yalnız ID ile referanslanır (nav property YOK).
    public virtual Guid? FollowingUnitId { get; protected set; }

    /// <summary>Parent'ın fiyatına uygulanan margin (yalnız follow varken anlamlı).</summary>
    public virtual MarginSetting? FollowingMargin { get; protected set; }

    /// <summary>Bu birim başka bir birimi takip ediyor mu (parent'tan fiyatlanıyor mu)?</summary>
    public virtual bool IsFollowing
    {
        get { return FollowingUnitId.HasValue; }
    }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.NormalizeName(
            name,
            nameof(Name),
            EntityFieldConsts.NameMinLength,
            CurrencyConsts.NameMaxLength);
    }

    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description,
            nameof(Description),
            EntityFieldConsts.DescriptionMinLength,
            CurrencyConsts.DescriptionMaxLength);
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetDisplayOrder(int order)
    {
        DisplayOrder = StringFieldGuard.EnsureRange(
            order,
            nameof(DisplayOrder),
            EntityFieldConsts.DisplayOrderMin,
            EntityFieldConsts.DisplayOrderMax);
    }

    public virtual void SetAlwaysShowInBalance(bool value)
    {
        AlwaysShowInBalance = value;
    }

    /// <summary>
    /// Takip ilişkisini ayarlar/temizler. Kendini takip edemez. Tek-seviye kuralı
    /// (takip edilen birim kendisi takip-eden olamaz) manager'da repo ile doğrulanır —
    /// entity yalnız self-follow'u ve margin zorunluluğunu engeller.
    /// </summary>
    public virtual void SetFollowing(Guid? followingUnitId, MarginSetting? followingMargin)
    {
        EnsureNotFollowingItself(followingUnitId);
        EnsureFollowingMarginProvided(followingUnitId, followingMargin);

        FollowingUnitId = followingUnitId;
        FollowingMargin = followingUnitId.HasValue ? followingMargin : null;
    }

    private void EnsureNotFollowingItself(Guid? followingUnitId)
    {
        if (followingUnitId.HasValue && followingUnitId.Value == Id)
        {
            throw new BusinessException("TradeXpress:CurrencyUnit:CannotFollowItself");
        }
    }

    private static void EnsureFollowingMarginProvided(Guid? followingUnitId, MarginSetting? followingMargin)
    {
        if (followingUnitId.HasValue && followingMargin is null)
        {
            throw new RequiredPropertyException(nameof(FollowingMargin));
        }
    }

    // Type set-once (UpdateDto'da yok) → public mutator YOK; yalnız ctor için private.
    private void SetType(CurrencyUnitType type)
    {
        Type = type;
    }

    public override string ToString()
    {
        return Code;
    }

    // Kod TENANT birimlerinde DÜZENLENEBİLİR (ürün kuralı 2026-07-04); HOST (TenantId==null) biriminin kodu
    // DEĞİŞTİRİLEMEZ — kilit AppService'te (HostCodeLocked; Cash seed'i ve türetmeler host koduna bağlı).
    // Normalize + min/max recheck StringFieldGuard'da; benzersizlik kontrolü AppService'te (TenantId scope).
    public virtual void SetCode(string code)
    {
        Code = StringFieldGuard.NormalizeCode(
            code,
            nameof(Code),
            EntityFieldConsts.CodeMinLength,
            CurrencyConsts.CodeMaxLength);
    }

    #endregion
}
