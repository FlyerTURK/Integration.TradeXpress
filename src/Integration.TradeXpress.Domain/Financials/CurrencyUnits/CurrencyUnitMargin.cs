namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// Bir tenant'ın (host=null dahil) bir <see cref="CurrencyUnit"/> için alış/satış marjı.
/// <b>Append-only</b> (<see cref="CreationAuditedAggregateRoot{TKey}"/>): marj değişimi
/// yeni satır INSERT eder — güncel marj = (TenantId, CompanyId, CurrencyUnitId) için en son
/// <c>CreationTime</c>. Asla UPDATE/DELETE yok. Böylece marj zaman-çizelgesi tutulur →
/// "geçmiş efektif = piyasa × o an aktif marj" doğrudan hesaplanır.
///
/// <para>Per-tenant (IMultiTenant) + <b>per-company</b> — TenantId ABP tarafından <c>CurrentTenant</c>'tan
/// atanır (ctor'da YOK). <see cref="CompanyId"/>: host (TenantId=null) → null (global taban); tenant →
/// working company ZORUNLU (HQ garantisi). <b>Branch bazlı DEĞİL.</b> Tutarlılık (tenant→company şart)
/// AppService'te denetlenir (CurrentTenant orada erişilir). Kimlik/following <see cref="CurrencyUnit"/>'te.</para>
/// </summary>
public class CurrencyUnitMargin : CreationAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected CurrencyUnitMargin()
    {
    }

    public CurrencyUnitMargin(
        Guid currencyUnitId,
        Guid? companyId,
        MarginSetting? marginOnBuy = null,
        MarginSetting? marginOnSell = null)
    {
        CurrencyUnitId = currencyUnitId;
        CompanyId = companyId;
        // Immutable: değerler ctor'da set edilir; değişim yeni satırla yapılır.
        MarginOnBuy = marginOnBuy ?? MarginSetting.Passthrough;
        MarginOnSell = marginOnSell ?? MarginSetting.Passthrough;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Marj'ın ait olduğu şirket. Host → null (global taban); tenant → working company (HQ garantisi
    /// ile daima dolu). Branch bazlı DEĞİL — şirketin tüm şubeleri aynı marjı görür.</summary>
    public virtual Guid? CompanyId { get; protected set; }

    /// <summary>Global <see cref="CurrencyUnit"/>'e referans (id-only, nav YOK — aggregate sınırı).</summary>
    public virtual Guid CurrencyUnitId { get; protected set; }

    public virtual MarginSetting MarginOnBuy { get; protected set; } = MarginSetting.Passthrough;
    public virtual MarginSetting MarginOnSell { get; protected set; } = MarginSetting.Passthrough;

    #endregion
}
