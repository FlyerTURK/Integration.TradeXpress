using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Vaults;
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
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsHeadquarters { get; set; }
    // IsActive: ana grid kolonu kaldırıldı ama Company drill list'i (BranchTreeItemViewModel)
    // bu listeden besleniyor ve durumu gösteriyor; bu yüzden DTO'da kalır.
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Combo kapalı gösterimi: "ŞirketKodu / ŞubeKodu".</summary>
    public string CompanyBranchCode => $"{CompanyCode} / {Code}";

    /// <summary>Combo 1. kolon: "ŞirketKodu / ŞirketAdı".</summary>
    public string CompanyDisplay => $"{CompanyCode} / {CompanyName}";

    /// <summary>Combo 2. kolon: "ŞubeKodu / ŞubeAdı".</summary>
    public string BranchDisplay => $"{Code} / {Name}";
}

public class BranchGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid CompanyId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;

    // VALİDASYON kuralları BURADA (tek kaynak) — BranchGraphDto bunlardan MİRAS alır → standalone ve
    // company-node şube düzenlemeleri GARANTİLİ aynı kuralları doğrular.
    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan kasalar (graf düğümleri; durum = Id + IsDeleted). Edit formu in-memory yönetir.
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

public class BranchCreateDto : ICreateDto
{
    [Required]
    public Guid CompanyId { get; set; }

    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan kasalar (graf) — tek komutta yazılır (VaultAppService'e delege).
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

// Parent (CompanyId) güncellemede değişmez — hiyerarşi sabit.
public class BranchUpdateDto : IUpdateDto
{
    [Required]
    [StringLength(BranchConsts.CodeMaxLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(BranchConsts.NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    public bool IsHeadquarters { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }

    [StringLength(BranchConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    // Sahip olunan kasalar (graf; Id+IsDeleted ile diff) — tek komutta yazılır (VaultAppService'e delege).
    public List<VaultGraphDto> Vaults { get; set; } = new();
}

/// <summary>
/// Company grafının şube DÜĞÜMÜ — Company edit'inde in-memory drill + Company save'i içindir (kendi
/// app servisi YOK; standalone Branch CRUD ayrı: <see cref="BranchGetDto"/> vb.). Durum = <see cref="Id"/>
/// + <see cref="IsDeleted"/>: Id boş → ekle, IsDeleted → sil, aksi → güncelle. Kasalar <see cref="Vaults"/>.
/// </summary>
public class BranchGraphDto : BranchGetDto
{
    // Graf düğümü EKSTRALARI (durum). Code/Name/Vaults + TÜM VALİDASYON BranchGetDto'dan MİRAS → standalone
    // ve company-node şube düzenlemeleri tek kaynaktan, GARANTİLİ aynı (kopya yok). (K3: GraphDto : GetDto)
    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }
}
