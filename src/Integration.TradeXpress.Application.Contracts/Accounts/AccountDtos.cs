using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Accounts;

/// <summary>Account liste sorgusu (per-tenant). Company-scoped: <see cref="CompanyId"/> ile daraltılır.</summary>
public class AccountListRequestDto : ListRequestDto
{
    /// <summary>Yalnızca bu şirkete ait hesaplar (company-scoped gösterim).</summary>
    public Guid? CompanyId { get; set; }
}

public class AccountListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public Guid BalanceCurrencyUnitId { get; set; }
    public string? BalanceCurrencyCode { get; set; }

    public decimal Limit { get; set; }
    public Guid LimitUnitId { get; set; }
    public string? LimitCurrencyCode { get; set; }

    public bool IsActive { get; set; }
}

public class AccountGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? BalanceCurrencyUnitId { get; set; }
    public string? BalanceCurrencyCode { get; set; }

    public decimal Limit { get; set; }

    [Required]
    public Guid? LimitUnitId { get; set; }
    public string? LimitCurrencyCode { get; set; }

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Alt hesaplar (graf düğümleri; Id + IsDeleted ile diff). Account edit formundaki drill yönetir.</summary>
    public List<SubAccountGraphDto> SubAccounts { get; set; } = new();
}

public class AccountCreateDto : ICreateDto
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [StringLength(AccountConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? BalanceCurrencyUnitId { get; set; }

    public decimal Limit { get; set; }

    [Required]
    public Guid? LimitUnitId { get; set; }

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public List<SubAccountGraphDto> SubAccounts { get; set; } = new();
}

public class AccountUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid? BalanceCurrencyUnitId { get; set; }

    public decimal Limit { get; set; }

    [Required]
    public Guid? LimitUnitId { get; set; }

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public List<SubAccountGraphDto> SubAccounts { get; set; } = new();
}
