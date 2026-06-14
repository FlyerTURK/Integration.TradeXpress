using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir döviz/maden ÇİFTİ (parite panosu satırı): <b>1 Base = X Quote</b>. Yön çift
/// konvansiyonudur (<see cref="CurrencyUnitPriority"/>): yüksek öncelikli birim Base
/// (USDTRY→base USD, EURUSD→base EUR, HASUSD→base HAS).
///
/// <para>Parity yalnız <b>çift tanımıdır</b> — oran burada saklanmaz, kendi marjı YOKTUR.
/// Canlı oran okuma anında birimlerin <b>efektif fiyatının saf çaprazından</b> türetilir
/// (kademe + gizlilik otomatik miras). Base/Quote id-only referans (nav YOK — aggregate sınırı,
/// kod/ad AppService'te join'lenir). Host=null+IsSystem seed; tenant kendi paritesini ekleyebilir.</para>
/// </summary>
public class Parity : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Çiftin base'i (1 base = X quote) — yüksek öncelikli birim.</summary>
    public virtual Guid BaseCurrencyUnitId { get; protected set; }

    /// <summary>Çiftin karşı (quote) birimi.</summary>
    public virtual Guid QuoteCurrencyUnitId { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    /// <summary>Sistem-seed parite: tenant düzenleyemez/silemez.</summary>
    public virtual bool IsSystem { get; protected set; }

    public virtual int DisplayOrder { get; protected set; }

    protected Parity() { }

    public Parity(
        Guid id,
        Guid baseCurrencyUnitId,
        Guid quoteCurrencyUnitId,
        bool isSystem = false,
        bool isActive = true,
        int displayOrder = 0,
        Guid? tenantId = null)
        : base(id)
    {
        if (baseCurrencyUnitId == quoteCurrencyUnitId)
            throw new InvalidOperationException("A parity's base and quote currency units must differ.");
        if (baseCurrencyUnitId == Guid.Empty || quoteCurrencyUnitId == Guid.Empty)
            throw new ArgumentException("Base and quote currency units are required.");

        BaseCurrencyUnitId = baseCurrencyUnitId;
        QuoteCurrencyUnitId = quoteCurrencyUnitId;
        IsSystem = isSystem;
        IsActive = isActive;
        DisplayOrder = displayOrder;
        TenantId = tenantId;
    }

    public virtual void Activate() => IsActive = true;
    public virtual void Deactivate() => IsActive = false;
    public virtual void SetDisplayOrder(int order) => DisplayOrder = order;
}
