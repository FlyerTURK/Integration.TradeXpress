using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// Fiş (muhasebe hareketi) — <b>company+branch+vault scoped</b>, per-tenant (IMultiTenant).
/// VoucherNumber şirket bazında otomatik artan uzun sayı.
/// Tüm kapsam alanları (Company/Branch/Vault/Account/SubAccount) oluşturmadan sonra değişmez.
/// VoucherDate: kullanıcı girişi (CreationTime'dan bağımsız), saniye hassasiyetinde saklanır.
/// </summary>
public class Voucher : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected Voucher()
    {
    }

    public Voucher(
        Guid companyId,
        Guid branchId,
        Guid? vaultId,
        AccountType accountType,
        Guid accountId,
        string accountCode,
        Guid subAccountId,
        string subAccountCode,
        long voucherNumber,
        DateTime voucherDate,
        string? description = null)
    {
        SetCompanyId(companyId);
        SetBranchId(branchId);
        VaultId = vaultId == Guid.Empty ? null : vaultId;
        SetCounterparty(accountType, accountId, accountCode, subAccountId, subAccountCode);
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

    /// <summary>Karşı taraf TİPİ — karşı-taraf alanlarının ANLAMINI belirler (polimorfik; legacy ERPPRO
    /// <c>HesapType</c> paritesi). Varsayılan <see cref="Vouchers.AccountType.CurrentAccount"/> (=0) →
    /// mevcut fişler backfill'siz doğru. Oluşturmadan sonra değişmez.</summary>
    public virtual AccountType AccountType { get; protected set; }

    /// <summary>Karşı tarafın ÜST kimliği — <b>tipe göre polimorfik</b> (2026-07-15 ürün kararı):
    /// <see cref="Vouchers.AccountType.CurrentAccount"/> → <c>Account.Id</c> ·
    /// <see cref="Vouchers.AccountType.Vault"/> → <c>Branch.Id</c>.
    /// <para><b>id-only snapshot — navigation/FK YOK</b> (VoucherLine'daki emtia alanlarıyla aynı desen):
    /// tek kolon iki farklı tabloya işaret ettiği için FK kurulamaz; bütünlük tip+guard ile korunur.</para></summary>
    public virtual Guid AccountId { get; protected set; }

    /// <summary>Karşı tarafın üst kimliğinin KOD SNAPSHOT'ı (Account.Code ‖ Branch.Code) — kayıt anında
    /// dondurulur; kaynak sonradan yeniden adlandırılsa da fişin gösterdiği kod değişmez.</summary>
    public virtual string AccountCode { get; protected set; } = string.Empty;

    /// <summary>Karşı tarafın ALT kimliği — <b>tipe göre polimorfik</b>:
    /// <see cref="Vouchers.AccountType.CurrentAccount"/> → <c>SubAccount.Id</c> ·
    /// <see cref="Vouchers.AccountType.Vault"/> → <c>Vault.Id</c>.
    /// <para>Okuma yolları (liste/ekstre/bakiye) DAİMA bu alanla anahtarlanır → kasa bakiyeleri sahte cari
    /// üretilmeden, sorgu imzaları değişmeden ayrışır.</para></summary>
    public virtual Guid SubAccountId { get; protected set; }

    /// <summary>Karşı tarafın alt kimliğinin KOD SNAPSHOT'ı (SubAccount.Code ‖ Vault.Code).</summary>
    public virtual string SubAccountCode { get; protected set; } = string.Empty;

    /// <summary>Şirket bazında otomatik artan fiş numarası.</summary>
    public virtual long VoucherNumber { get; protected set; }

    /// <summary>Kullanıcı girişi fiş tarihi+saati — saniye hassasiyetinde, CreationTime'dan bağımsız.
    /// <para><b>Wall-clock (kaymasız):</b> <c>[DisableDateTimeNormalization]</c> ile ABP <c>IClock</c> (UTC)
    /// bu değeri UTC'ye çevirmez. Giriş <see cref="BusinessClock.Now"/> ile Kind=Unspecified gelir,
    /// <see cref="TruncateToSeconds"/> Kind'ı garanti eder → gün/saat kayması yok, ekstre/gün-sınırı stabil.</para></summary>
    [DisableDateTimeNormalization]
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
    public virtual void SetVoucherNumber(long number)
    {
        VoucherNumber = number;
    }

    /// <summary>Yeni satır ekler (Id dışarıdan — IGuidGenerator).</summary>
    public virtual VoucherLine AddLine(Guid id, VoucherLineInput input)
    {
        var line = new VoucherLine(id, Id, input);
        Lines.Add(line);
        return line;
    }

    /// <summary>Mevcut satırın alanlarını günceller.</summary>
    public virtual void UpdateLine(Guid lineId, VoucherLineInput input)
    {
        Lines.FirstOrDefault(l => l.Id == lineId && !l.IsDeleted)?.Set(input);
    }

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

    /// <summary>Fişin karşı tarafını (tip + id'ler + kod snapshot'ları) kurar ve DEĞİŞMEZİ zorlar (fail-fast).
    /// <para>Dört alan da TİPTEN BAĞIMSIZ ZORUNLUDUR: cari fişte Account/SubAccount, kasa fişinde Şube/Kasa
    /// doldurur. Şema tek olduğu için okuma yolları (liste/ekstre/bakiye) tipe bakmadan, imza değiştirmeden
    /// çalışır; kasa bakiyeleri sahte cari ÜRETİLMEDEN ayrışır (2026-07-15 ürün kararı).</para></summary>
    private void SetCounterparty(
        AccountType accountType, Guid accountId, string accountCode, Guid subAccountId, string subAccountCode)
    {
        if (accountId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(AccountId));
        }

        if (subAccountId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SubAccountId));
        }

        AccountType    = accountType;
        AccountId      = accountId;
        AccountCode    = StringFieldGuard.NormalizeCode(
            accountCode, nameof(AccountCode), VoucherConsts.CounterpartyCodeMinLength, VoucherConsts.CounterpartyCodeMaxLength);
        SubAccountId   = subAccountId;
        SubAccountCode = StringFieldGuard.NormalizeCode(
            subAccountCode, nameof(SubAccountCode), VoucherConsts.CounterpartyCodeMinLength, VoucherConsts.CounterpartyCodeMaxLength);
    }

    /// <summary>Saniye-altını atar VE Kind'ı <see cref="DateTimeKind.Unspecified"/>'e sabitler:
    /// girişte Local/Utc gelse bile ABP bunu normalize edip kaydırmasın (alan zaten
    /// <c>[DisableDateTimeNormalization]</c>; bu, wall-clock garantisinin entity-içi SSOT parçasıdır).</summary>
    private static DateTime TruncateToSeconds(DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, DateTimeKind.Unspecified);
    }

    #endregion
}
