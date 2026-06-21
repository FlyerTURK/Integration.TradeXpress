using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir fiyatlama/kayıt birimi: nakit döviz (USD, TRY) ya da maden (HAS, GUM).
/// Birim <b>kimliktir</b> — piyasa fiyatı burada DEĞİL (ExchangeRate + cache'te yaşar).
/// Burada yalnız kimlik + <b>türetme kuralı</b> (margin/follow config) tutulur.
///
/// <para>Re-basing (ABD şirketinde USD=1) bu entity'de değil; aktif Company.BaseCurrencyUnitId
/// ile <see cref="CurrencyPriceCalculator.ReBase"/> tarafından yapılır.</para>
/// </summary>
public class CurrencyUnit : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
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

    /// <summary>Sistem-seed birim: Code değiştirilemez, silinemez.</summary>
    public virtual bool IsSystem { get; protected set; }

    // ── Takip (follow) ilişkisi — YAPISAL/GLOBAL (feed-seviyesi türetme) ───────
    // Alış/satış marjı burada DEĞİL — o per-tenant CurrencyUnitMargin'de yaşar.
    // Following ise kimliğin parçası: "PLD, HAS'tan şu margin'le türetilir" herkes için aynı.
    // Aggregate sınırı: parent yalnız ID ile referanslanır (nav property YOK) — başka
    // aggregate'i bu aggregate'in grafiğine çekmemek + EF self-FK derdini önlemek için.
    public virtual Guid? FollowingUnitId { get; protected set; }

    /// <summary>Parent'ın fiyatına uygulanan margin (yalnız follow varken anlamlı).</summary>
    public virtual MarginSetting? FollowingMargin { get; protected set; }

    protected CurrencyUnit() { }

    public CurrencyUnit(
        Guid id,
        string code,
        string name,
        CurrencyUnitType type = CurrencyUnitType.Cash,
        bool isSystem = false,
        int displayOrder = 0)
        : base(id)
    {
        SetCodeInternal(code);
        SetName(name);
        Type = type;
        IsSystem = isSystem;
        DisplayOrder = displayOrder;
        IsActive = true;
    }

    public virtual void SetCode(string code)
    {
        if (IsSystem)
            throw new InvalidOperationException("Cannot change the code of a system currency unit.");
        SetCodeInternal(code);
    }

    private void SetCodeInternal(string code)
        => Code = Check.NotNullOrWhiteSpace(code, nameof(code), CurrencyConsts.CodeMaxLength);

    public virtual void SetName(string name)
        => Name = Check.NotNullOrWhiteSpace(name, nameof(name), CurrencyConsts.NameMaxLength);

    public virtual void SetDescription(string? description)
    {
        if (description is { Length: > CurrencyConsts.DescriptionMaxLength })
            throw new ArgumentException(
                $"Description length must be at most {CurrencyConsts.DescriptionMaxLength}.", nameof(description));
        Description = description;
    }

    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() => IsActive = false;
    public virtual void SetDisplayOrder(int order) => DisplayOrder = order;
    public virtual void SetAlwaysShowInBalance(bool value) => AlwaysShowInBalance = value;

    /// <summary>
    /// Takip ilişkisini ayarlar/temizler. Kendini takip edemez. Tek-seviye kuralı
    /// (takip edilen birim kendisi takip-eden olamaz) AppService/Manager'da repo ile
    /// doğrulanır — entity yalnız self-follow'u engeller.
    /// </summary>
    public virtual void SetFollowing(Guid? followingUnitId, MarginSetting? followingMargin)
    {
        if (followingUnitId.HasValue && followingUnitId.Value == Id)
            throw new InvalidOperationException("A currency unit cannot follow itself.");

        if (followingUnitId.HasValue && followingMargin is null)
            throw new ArgumentNullException(nameof(followingMargin), "Following margin is required when a following unit is set.");

        FollowingUnitId = followingUnitId;
        FollowingMargin = followingUnitId.HasValue ? followingMargin : null;
    }

    /// <summary>Bu birim başka bir birimi takip ediyor mu (parent'tan fiyatlanıyor mu)?</summary>
    public virtual bool IsFollowing => FollowingUnitId.HasValue;
}
