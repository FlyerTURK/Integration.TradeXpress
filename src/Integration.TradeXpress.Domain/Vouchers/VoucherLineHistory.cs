namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// <b>Ekleme-sadece</b> (append-only) fiş satırı değişim günlüğü — <see cref="VoucherLine"/>'ın her
/// ekle/güncelle/sil anındaki SNAPSHOT'I. Application katmanında (<c>VoucherLineHistoryRecorder</c>) satır
/// işlemiyle AYNI UoW/transaction içinde yazılır; DOMAIN'in asıl akışının (posting/bakiye) yanında yaşayan
/// GÖLGE günlüktür — mevcut davranışı DEĞİŞTİRMEZ.
///
/// <para>Hiçbir zaman güncellenmez/silinmez → <see cref="CreationAuditedEntity{TKey}"/> yeterlidir
/// (<c>FullAudited</c>/<c>ISoftDelete</c> GEREKMEZ). "Ne zaman" = <c>CreationTime</c>,
/// "kim" = <c>CreatorId</c> (ABP zaten sağlar — ayrı alan DRY ihlali olurdu).</para>
///
/// <para><b>JSON pattern (Confirmation ile aynı):</b> tam satır <see cref="SnapshotJson"/>'da
/// serileştirilmiş <c>VoucherLineDto</c> olarak taşınır (popup detay gösterimi); skaler alanlar
/// grid/filtre için DENORMALİZE edilir (JSON deserialize etmeden sorgu).</para>
/// </summary>
public class VoucherLineHistory : CreationAuditedEntity<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected VoucherLineHistory()
    {
    }

    public VoucherLineHistory(
        Guid voucherLineId,
        Guid voucherId,
        Guid companyId,
        VoucherLineChangeType changeType,
        string voucherNumber,
        DateTime voucherDate,
        ProcessType processType,
        string processCode,
        string? commodityCode,
        decimal quantity,
        decimal amount,
        decimal total,
        string? mainUnitCode,
        string? description,
        Guid subAccountId,
        string snapshotJson)
    {
        VoucherLineId = voucherLineId;
        VoucherId     = voucherId;
        SetCompany(companyId);
        ChangeType    = changeType;
        VoucherNumber = StringFieldGuard.EnsureRequiredText(
            voucherNumber, nameof(VoucherNumber), 1, VoucherLineHistoryConsts.CommodityCodeMaxLength);
        VoucherDate   = voucherDate;
        ProcessType   = processType;
        ProcessCode   = StringFieldGuard.EnsureRequiredText(
            processCode, nameof(ProcessCode), 1, VoucherLineHistoryConsts.CommodityCodeMaxLength);
        CommodityCode = StringFieldGuard.EnsureOptionalText(
            commodityCode, nameof(CommodityCode), 0, VoucherLineHistoryConsts.CommodityCodeMaxLength);
        Quantity      = quantity;
        Amount        = amount;
        Total         = total;
        MainUnitCode  = StringFieldGuard.EnsureOptionalText(
            mainUnitCode, nameof(MainUnitCode), 0, VoucherLineHistoryConsts.MainUnitCodeMaxLength);
        Description   = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), 0, VoucherLineHistoryConsts.DescriptionMaxLength);
        SubAccountId  = subAccountId;
        SnapshotJson  = StringFieldGuard.EnsureRequiredText(
            snapshotJson, nameof(SnapshotJson), 1, VoucherLineHistoryConsts.SnapshotMaxLength);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket — sızıntı önleme (finansal çekirdek güvenlik sınırı).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Bu geçmişin ait olduğu fiş satırı — id-only referans (VoucherLine ayrı tablo DEĞİL,
    /// Voucher aggregate'inin owned koleksiyonu; FK kurulamaz, BalanceLedger deseniyle hizalı).</summary>
    public virtual Guid VoucherLineId { get; protected set; }

    /// <summary>Satırın ait olduğu fiş — id-only referans.</summary>
    public virtual Guid VoucherId { get; protected set; }

    /// <summary>Bu anlık görüntünün hangi işlemin sonucu olduğu (ekle/güncelle/sil).</summary>
    public virtual VoucherLineChangeType ChangeType { get; protected set; }

    // ── Denormalize skaler alanlar (grid/filtre — JSON deserialize GEREKMEZ) ──

    public virtual string VoucherNumber { get; protected set; } = string.Empty;

    public virtual DateTime VoucherDate { get; protected set; }

    public virtual ProcessType ProcessType { get; protected set; }

    public virtual string ProcessCode { get; protected set; } = string.Empty;

    public virtual string? CommodityCode { get; protected set; }

    public virtual decimal Quantity { get; protected set; }

    public virtual decimal Amount { get; protected set; }

    public virtual decimal Total { get; protected set; }

    public virtual string? MainUnitCode { get; protected set; }

    public virtual string? Description { get; protected set; }

    /// <summary>Karşı taraf alt kimliği (polimorfik: cari kipinde SubAccount, kasa kipinde Kasa) —
    /// Log tab filtresi bununla anahtarlanır (Voucher.SubAccountId ile aynı desen).</summary>
    public virtual Guid SubAccountId { get; protected set; }

    /// <summary>Tam satırın (<c>VoucherLineDto</c>) o anki serileştirilmiş hâli — popup detay kaynağı.</summary>
    public virtual string SnapshotJson { get; protected set; } = string.Empty;

    #endregion

    #region Methods

    public override string ToString()
    {
        return $"{base.ToString()}, {VoucherNumber} {ProcessCode} [{ChangeType}]";
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:VoucherLineHistory:CompanyRequired");
        }

        CompanyId = companyId;
    }

    #endregion
}
