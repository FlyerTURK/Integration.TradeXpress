using System;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Vaults;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Vaults;

/// <summary>Vault liste sorgusu (per-tenant). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class VaultListRequestDto : ListRequestDto
{
    /// <summary>Drill-down filtresi: yalnızca bu şubeye ait kasalar. GET'te scalar serialize olur.</summary>
    public Guid? BranchId { get; set; }
}

public class VaultListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    // IsActive: ana grid kolonu kaldırıldı ama Company drill list'i (VaultTreeItemViewModel)
    // bu listeden besleniyor ve durumu gösteriyor; bu yüzden DTO'da kalır.
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class VaultGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }

    public int PageIndex { get; set; }
}

public class VaultCreateDto : ICreateDto
{
    [Required]
    public Guid BranchId { get; set; }

    [Required]
    [StringLength(VaultConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VaultConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(VaultConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

// Parent (BranchId) güncellemede değişmez — hiyerarşi sabit.
public class VaultUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(VaultConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(VaultConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(VaultConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
