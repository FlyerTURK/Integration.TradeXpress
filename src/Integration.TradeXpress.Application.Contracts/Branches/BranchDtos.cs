using System;
using System.ComponentModel.DataAnnotations;
using Integration.TradeXpress.Branches;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Branches;

/// <summary>Branch liste sorgusu (per-tenant). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class BranchListRequestDto : ListRequestDto
{
    /// <summary>Drill-down filtresi: yalnızca bu şirkete ait şubeler. GET'te scalar serialize olur.</summary>
    public Guid? CompanyId { get; set; }
}

public class BranchListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHeadquarters { get; set; }
    // IsActive: ana grid kolonu kaldırıldı ama Company drill list'i (BranchTreeItemViewModel)
    // bu listeden besleniyor ve durumu gösteriyor; bu yüzden DTO'da kalır.
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
}

public class BranchGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public string? Description { get; set; }

    public int PageIndex { get; set; }
}

public class BranchCreateDto : ICreateDto
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

// Parent (CompanyId) güncellemede değişmez — hiyerarşi sabit.
public class BranchUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
