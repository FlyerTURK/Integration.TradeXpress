using System;
using Volo.Abp.Domain.Entities.Auditing;

namespace Integration.TradeXpress.N11Categories;

/// <summary>
/// N11 kategori ağacı düğümü — <b>HOST-GLOBAL</b> referans taksonomi. <see cref="IMultiTenant"/> DEĞİL (TenantId
/// YOK) → tüm tenant'lar aynı ağacı paylaşır; host bir kez REST <c>/cdn/categories</c>'ten sync'ler (kullanıcı
/// kararı 2026-07-06). Attribute/value SAKLANMAZ — bir yaprak kategori seçilince ilgili SalesChannel'ın KENDİ
/// AppKey/AppSecret'ıyla on-demand çekilir. Ürünler yalnız <see cref="IsLeaf"/> (last-level) düğüme tanımlanır.
/// </summary>
public class N11Category : FullAuditedAggregateRoot<Guid>
{
    #region Constructors

    protected N11Category()
    {
    }

    public N11Category(string externalId, string? parentExternalId, string name, bool isLeaf, DateTime? lastModifiedExternal)
    {
        // ExternalId set-once (N11'den gelir): normalize YOK, yalnız null/uzunluk guard'ı (kimlik, matematik değil).
        ExternalId = StringFieldGuard.EnsureRequiredText(externalId, nameof(ExternalId), 1, N11CategoryConsts.ExternalIdMaxLength);
        SetParent(parentExternalId);
        SetName(name);
        IsLeaf = isLeaf;
        LastModifiedExternal = lastModifiedExternal;
    }

    #endregion

    #region Properties

    /// <summary>N11 kategori id'si (numerik ama matematik değil → string). Global benzersiz.</summary>
    public string ExternalId { get; protected set; } = string.Empty;

    /// <summary>Üst kategori N11 id'si (79 top'ta null). Ağaç REST <c>parentId</c>'sinden kurulur.</summary>
    public string? ParentExternalId { get; protected set; }

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Dip seviye (last-level) mi — ürün yalnız buraya tanımlanır; attribute yalnız burada.</summary>
    public bool IsLeaf { get; protected set; }

    /// <summary>N11'in <c>lastModifiedDate</c>'i (SOAP) — incremental sync için (REST bunu vermez).</summary>
    public DateTime? LastModifiedExternal { get; protected set; }

    // ── Komisyon (YALNIZ yaprak/last-level kategoride dolu; ara/üst kategoride null). n11-commission.tsv'den. ──

    /// <summary>Güncel komisyon oranı (%, KDV DAHİL). Yaprakta dolu; matematik yapılır → decimal.</summary>
    public decimal? CommissionRate { get; protected set; }

    /// <summary>Pazarlama hizmet bedeli oranı (%, KDV hariç — ör. 1 ya da 0.17).</summary>
    public decimal? MarketingFeeRate { get; protected set; }

    /// <summary>Pazaryeri hizmet bedeli oranı (%, KDV hariç — pratikte sabit 0.67).</summary>
    public decimal? MarketplaceFeeRate { get; protected set; }

    /// <summary>Hakediş hesaplama süresi (iş günü) = "otomatik bloke çözme günü" (valör).</summary>
    public int? PayoutDays { get; protected set; }

    #endregion

    #region Methods

    public virtual void SetName(string name)
    {
        Name = StringFieldGuard.EnsureRequiredText(name, nameof(Name), 1, N11CategoryConsts.NameMaxLength);
    }

    public virtual void SetParent(string? parentExternalId)
    {
        ParentExternalId = string.IsNullOrWhiteSpace(parentExternalId) ? null : parentExternalId.Trim();
    }

    public virtual void SetIsLeaf(bool isLeaf)
    {
        IsLeaf = isLeaf;
    }

    public virtual void SetLastModifiedExternal(DateTime? lastModifiedExternal)
    {
        LastModifiedExternal = lastModifiedExternal;
    }

    /// <summary>Yaprak kategorinin komisyon/valör bilgisini set eder (n11-commission.tsv import'u). Ara/üst kategoride çağrılmaz.</summary>
    public virtual void SetCommission(decimal? commissionRate, decimal? marketingFeeRate, decimal? marketplaceFeeRate, int? payoutDays)
    {
        CommissionRate = commissionRate;
        MarketingFeeRate = marketingFeeRate;
        MarketplaceFeeRate = marketplaceFeeRate;
        PayoutDays = payoutDays;
    }

    public override string ToString()
    {
        return Name;
    }

    #endregion
}
