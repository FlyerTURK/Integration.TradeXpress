using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Timing;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Vouchers;

public class VoucherCreateDto
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    public Guid BranchId { get; set; }

    public Guid? VaultId { get; set; }

    /// <summary>Karşı taraf tipi — alttaki id/kod alanlarının ANLAMINI belirler. Varsayılan cari (dış akış).</summary>
    public AccountType AccountType { get; set; } = AccountType.CurrentAccount;

    /// <summary>Üst kimlik — cari kipinde Account, kasa kipinde ŞUBE id'si.</summary>
    [Required]
    public Guid AccountId { get; set; }

    /// <summary>Alt kimlik — cari kipinde SubAccount, kasa kipinde KASA id'si. DTO'da nullable: form
    /// "henüz seçilmedi" halini taşır. Fişte ZORUNLUDUR (Voucher.SetCounterparty guard'ı).</summary>
    public Guid? SubAccountId { get; set; }

    [Required]
    public DateTime VoucherDate { get; set; } = BusinessClock.Now();

    [StringLength(VoucherConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class VoucherGetDto : EntityDto<Guid>
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? VaultId { get; set; }
    public AccountType AccountType { get; set; }
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public Guid SubAccountId { get; set; }
    public string SubAccountCode { get; set; } = string.Empty;
    public long VoucherNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public string? Description { get; set; }
}

public class VoucherListRequestDto
{
    /// <summary>Karşı taraf anahtarı — POLİMORFİK: cari kipinde SubAccount, iç kasa kipinde KASA id'si.
    /// Kip ayrımı için ek alan GEREKMEZ (fişin AccountType'ı zaten kaydın kendisinde).</summary>
    public Guid? SubAccountId { get; set; }

    public int SkipCount { get; set; }
    public int MaxResultCount { get; set; } = 1000;
}

public class VoucherListDto : EntityDto<Guid>
{
    public long VoucherNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string? VaultCode { get; set; }
    public string? Description { get; set; }
    public int LineCount { get; set; }

    public string VaultDisplay => VaultCode != null ? $"{BranchCode}/{VaultCode}" : BranchCode;
}
