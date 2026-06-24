using System;
using System.Collections.Generic;
using System.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş (muhasebe hareketi) — <b>company+branch+vault scoped</b>, per-tenant (IMultiTenant).
/// VoucherNumber şirket bazında otomatik artan uzun sayı.
/// Tüm kapsam alanları (Company/Branch/Vault/Account/SubAccount) oluşturmadan sonra değişmez.
/// VoucherDate: kullanıcı girişi (CreationTime'dan bağımsız), saniye hassasiyetinde saklanır.
/// </summary>
public class Voucher : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    #region Constructors

    protected Voucher()
    {
    }

    public Voucher(
        Guid companyId,
        Guid branchId,
        Guid? vaultId,
        Guid accountId,
        Guid? subAccountId,
        long voucherNumber,
        DateTime voucherDate,
        string? description = null)
    {
        SetCompanyId(companyId);
        SetBranchId(branchId);
        VaultId = vaultId == Guid.Empty ? null : vaultId;
        SetAccountId(accountId);
        SubAccountId = subAccountId == Guid.Empty ? null : subAccountId;
        VoucherNumber = voucherNumber;
        VoucherDate   = TruncateToSeconds(voucherDate);
        SetDescription(description);
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Şirket — oluşturmadan sonra değişmez.</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Şube — oluşturmadan sonra değişmez.</summary>
    public virtual Guid BranchId { get; protected set; }

    /// <summary>Kasa (opsiyonel) — oluşturmadan sonra değişmez.</summary>
    public virtual Guid? VaultId { get; protected set; }

    /// <summary>Cari hesap — oluşturmadan sonra değişmez.</summary>
    public virtual Guid AccountId { get; protected set; }

    /// <summary>Alt hesap (opsiyonel) — oluşturmadan sonra değişmez.</summary>
    public virtual Guid? SubAccountId { get; protected set; }

    /// <summary>Şirket bazında otomatik artan fiş numarası.</summary>
    public virtual long VoucherNumber { get; protected set; }

    /// <summary>Kullanıcı girişi fiş tarihi+saati — saniye hassasiyetinde, CreationTime'dan bağımsız.</summary>
    public virtual DateTime VoucherDate { get; protected set; }

    public virtual string? Description { get; protected set; }

    public virtual ICollection<VoucherLine> Lines { get; protected set; } = new List<VoucherLine>();

    #endregion

    #region Methods

    public virtual void SetDescription(string? value)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            value, nameof(Description), 0, VoucherConsts.DescriptionMaxLength);
    }

    /// <summary>Başlık alanlarını günceller (yapısal).</summary>
    public virtual void SetHeader(DateTime voucherDate, string? description)
    {
        VoucherDate = TruncateToSeconds(voucherDate);
        SetDescription(description);
    }

    /// <summary>Fiş numarasını dışarıdan atar (numara servisi; "ne zaman" kararı burada değil).</summary>
    public virtual void SetVoucherNumber(long number) => VoucherNumber = number;

    /// <summary>Yeni satır ekler (Id dışarıdan — IGuidGenerator).</summary>
    public virtual VoucherLine AddLine(Guid id, VoucherLineInput input)
    {
        var line = new VoucherLine(id, Id, input);
        Lines.Add(line);
        return line;
    }

    /// <summary>Mevcut satırın alanlarını günceller.</summary>
    public virtual void UpdateLine(Guid lineId, VoucherLineInput input)
        => Lines.FirstOrDefault(l => l.Id == lineId && !l.IsDeleted)?.Set(input);

    /// <summary>Satırı soft-delete eder (koleksiyondan çıkarmaz — DB'de kalır).</summary>
    public virtual void RemoveLine(Guid lineId)
    {
        var line = Lines.FirstOrDefault(l => l.Id == lineId && !l.IsDeleted);
        if (line != null)
            line.IsDeleted = true;
    }

    private void SetCompanyId(Guid value)
    {
        if (value == Guid.Empty) throw new RequiredPropertyException(nameof(CompanyId));
        CompanyId = value;
    }

    private void SetBranchId(Guid value)
    {
        if (value == Guid.Empty) throw new RequiredPropertyException(nameof(BranchId));
        BranchId = value;
    }

    private void SetAccountId(Guid value)
    {
        if (value == Guid.Empty) throw new RequiredPropertyException(nameof(AccountId));
        AccountId = value;
    }

    private static DateTime TruncateToSeconds(DateTime dt)
        => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);

    #endregion
}
