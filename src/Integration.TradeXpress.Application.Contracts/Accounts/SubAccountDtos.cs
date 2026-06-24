using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Accounts;

/// <summary>SubAccount liste sorgusu (per-tenant). Branch-scoped + hesaba göre daraltılır.</summary>
public class SubAccountListRequestDto : ListRequestDto
{
    /// <summary>Yalnızca bu üst hesaba ait alt hesaplar.</summary>
    public Guid? AccountId { get; set; }
    /// <summary>Yalnızca bu şubeye ait alt hesaplar (branch-scoped gösterim).</summary>
    public Guid? BranchId { get; set; }
}

public class SubAccountListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? BranchCode { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public string AccountDisplay => $"{AccountCode} / {AccountName}";
    public string SubAccountDisplay => $"{Code} / {Name}";
    public string AccountSubCodeDisplay => $"{AccountCode} / {Code}";
}

public class SubAccountGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    public Guid? AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;

    /// <summary>Şube — OPSİYONEL; oluşturmadan sonra değişmez.</summary>
    public Guid? BranchId { get; set; }
    public string? BranchCode { get; set; }

    [Required]
    [StringLength(AccountConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}

public class SubAccountCreateDto : ICreateDto
{
    [Required]
    public Guid? AccountId { get; set; }

    /// <summary>Şube — OPSİYONEL (null olabilir). Oluşturmadan sonra değişmez.</summary>
    public Guid? BranchId { get; set; }

    [Required]
    [StringLength(AccountConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class SubAccountUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }
}

/// <summary>
/// Account grafının alt hesap DÜĞÜMÜ — Account edit'inde in-memory drill + Account save'i içindir.
/// Durum = <see cref="Id"/> + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil, aksi → güncelle.
/// (BranchId drill'de atanmaz; nullable şube ileride.)
/// </summary>
public class SubAccountGraphDto
{
    public Guid Id { get; set; }
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }

    [Required]
    [StringLength(AccountConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(AccountConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(AccountConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
}
