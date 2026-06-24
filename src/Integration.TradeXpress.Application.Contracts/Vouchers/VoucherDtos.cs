using System;
using System.ComponentModel.DataAnnotations;
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

    [Required]
    public Guid AccountId { get; set; }

    public Guid? SubAccountId { get; set; }

    [Required]
    public DateTime VoucherDate { get; set; } = DateTime.Now;

    [StringLength(VoucherConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class VoucherGetDto : EntityDto<Guid>
{
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? VaultId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? SubAccountId { get; set; }
    public long VoucherNumber { get; set; }
    public DateTime VoucherDate { get; set; }
    public string? Description { get; set; }
}

public class VoucherListRequestDto
{
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

    public string VaultDisplay => VaultCode != null ? $"{BranchCode}/{VaultCode}" : BranchCode;
}
