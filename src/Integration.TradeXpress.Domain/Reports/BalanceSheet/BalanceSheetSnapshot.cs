using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Financials;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Reports;

namespace Integration.TradeXpress.Reports.BalanceSheet;

/// <summary>
/// Bir bilançonun DONDURULMUŞ satır snapshot'ı (ERPPRO <c>Bilanco.Bilancolar</c> paritesi). <c>SaveAsync</c>,
/// hesaplanan bilançoyu (kategori×birim) buraya idempotent DELETE+INSERT ile yazar → geçmiş yeniden üretilebilir,
/// değerleme O ANIN kuruyla DONDURULUR (tek-yol re-base modeli: <see cref="ValuationRate"/> = donmuş kur; ayrı
/// Kur1/Kur2 sistemde tutulmaz, base÷base=1). Grain: (Scope, CompanyId, BranchId?, AsOfDate, Category, UnitId).
/// <para>FK YOK — CompanyId/BranchId/UnitId/BaseUnitId id-only mantıksal referans (rapor scope filtresi; ledger deseni).
/// TenantId nav'sız (ABP <see cref="IMultiTenant"/> insert'te otomatik basar).</para>
/// </summary>
public class BalanceSheetSnapshot : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected BalanceSheetSnapshot()
    {
    }

    /// <summary>Hesaplanan bir detay satırından dondurulmuş snapshot kurar. Id/TenantId ABP tarafından atanır.
    /// <para><b>Rounding:</b> KAYIT ANINDA <see cref="FinancialRounding"/> — Amount/Net N2, ValuationRate N5,
    /// AwayFromZero (ERPPRO <c>Bilanco.Bilancolar</c>: Bakiye/Net decimal(…,2), Kur1/Kur2 decimal(…,5);
    /// compute ara hesapları HAM kalır, yalnız kalıcılaşan değer yuvarlanır).</para></summary>
    public BalanceSheetSnapshot(
        BalanceSheetScope scope,
        Guid companyId,
        Guid? branchId,
        DateTime asOfDate,
        string category,
        Guid unitId,
        decimal amount,
        decimal valuationRate,
        decimal net,
        Guid baseUnitId,
        string baseCurrencyCode)
    {
        Scope            = scope;
        CompanyId        = companyId;
        BranchId         = branchId;
        AsOfDate         = BusinessClock.AsBusinessDate(asOfDate);   // yalnız gün + Kind=Unspecified (wall-clock, kaymasız)
        Category         = category;
        UnitId           = unitId;
        Amount           = FinancialRounding.RoundAmount(amount);
        ValuationRate    = FinancialRounding.RoundRate(valuationRate);
        Net              = FinancialRounding.RoundAmount(net);
        BaseUnitId       = baseUnitId;
        BaseCurrencyCode = baseCurrencyCode;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    // ── Kapsam (dondurulmuş bilançonun kimliği) ──
    /// <summary>Kapsam kademesi (Şube/Şirket) — dondurulan bilançonun hangi seviyede alındığı.</summary>
    public virtual BalanceSheetScope Scope { get; protected set; }

    /// <summary>Sahip şirket — id-only (nav YOK). Kapsam DAİMA çalışılan şirket (sunucu zorlar).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Şube — id-only; null = şirket konsolide snapshot.</summary>
    public virtual Guid? BranchId { get; protected set; }

    /// <summary>Bilanço tarihi — yalnız GÜN (saat yok); idempotent gün+kapsam yeniden-yazımının anahtarı.
    /// <para><b>Wall-clock (kaymasız):</b> <c>[DisableDateTimeNormalization]</c> ile ABP UTC'ye çevirmez; ctor
    /// <see cref="BusinessClock.AsBusinessDate"/> ile Kind=Unspecified günü sabitler → DELETE/INSERT ve unique index
    /// <c>(Scope,CompanyId,BranchId,AsOfDate)</c> aynı gün-değerini görür (idempotency korunur).</para></summary>
    [DisableDateTimeNormalization]
    public virtual DateTime AsOfDate { get; protected set; }

    // ── Satır kimliği (kategori × birim) ──
    /// <summary>Bilanço kategorisi (<see cref="BalanceSheetCategory"/> anahtarı; ör. "AccountBalance"/"Stock").</summary>
    public virtual string Category { get; protected set; } = null!;

    /// <summary>Satır para/emtia birimi — id-only (nav YOK).</summary>
    public virtual Guid UnitId { get; protected set; }

    // ── DONDURULMUŞ değerleme (kur dahil) ──
    /// <summary>Birimin kendi cinsinden bakiye/miktar (donmuş).</summary>
    public virtual decimal Amount { get; protected set; }

    /// <summary>Birimi base'e çeviren efektif değerleme kuru — DONMUŞ KUR (<c>Net = Amount × ValuationRate</c>).
    /// Tek-yol re-base modelinde ERPPRO Kur1/Kur2 çaprazının tek-kur karşılığı; ayrı Kur1/Kur2 tutulmaz.</summary>
    public virtual decimal ValuationRate { get; protected set; }

    /// <summary>Base (bilanço) birimine değerlenmiş, dondurulmuş karşılık (alış kuru). MissingRate satırında 0.</summary>
    public virtual decimal Net { get; protected set; }

    /// <summary>Bilanço (base) birimi — id-only.</summary>
    public virtual Guid BaseUnitId { get; protected set; }

    /// <summary>Bilanço (base) birimi kodu (gösterim/denetim için dondurulur).</summary>
    public virtual string BaseCurrencyCode { get; protected set; } = null!;

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{Category}/{UnitId} @ {AsOfDate:yyyy-MM-dd}";
    }

    #endregion
}
