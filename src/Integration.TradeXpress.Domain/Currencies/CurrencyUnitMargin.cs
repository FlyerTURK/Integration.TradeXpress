using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir tenant'ın (host=null dahil) bir <see cref="CurrencyUnit"/> için alış/satış marjı.
/// <b>Append-only</b> (<see cref="CreationAuditedAggregateRoot{TKey}"/>): marj değişimi
/// yeni satır INSERT eder — güncel marj = (TenantId, CurrencyUnitId) için en son
/// <c>CreationTime</c>. Asla UPDATE/DELETE yok. Böylece marj zaman-çizelgesi tutulur →
/// "geçmiş efektif = piyasa × o an aktif marj" doğrudan hesaplanır; ayrı marj→ExchangeRate
/// yazımına gerek kalmaz (okuma ham + marj olaylarını birleştirir).
///
/// <para>Per-tenant (IMultiTenant) — standart ABP filtresi yeterli. Kimlik ve yapısal
/// following <see cref="CurrencyUnit"/>'te (global); burada yalnız tenant'a özel marj.</para>
/// </summary>
public class CurrencyUnitMargin : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Global <see cref="CurrencyUnit"/>'e referans (id-only, nav YOK — aggregate sınırı).</summary>
    public virtual Guid CurrencyUnitId { get; protected set; }

    public virtual MarginSetting MarginOnBuy  { get; protected set; } = MarginSetting.Passthrough;
    public virtual MarginSetting MarginOnSell { get; protected set; } = MarginSetting.Passthrough;

    protected CurrencyUnitMargin() { }

    public CurrencyUnitMargin(
        Guid id,
        Guid currencyUnitId,
        MarginSetting? marginOnBuy = null,
        MarginSetting? marginOnSell = null,
        Guid? tenantId = null)
        : base(id)
    {
        CurrencyUnitId = currencyUnitId;
        TenantId       = tenantId;
        // Immutable: değerler ctor'da set edilir; değişim yeni satırla yapılır.
        MarginOnBuy  = marginOnBuy  ?? MarginSetting.Passthrough;
        MarginOnSell = marginOnSell ?? MarginSetting.Passthrough;
    }
}
