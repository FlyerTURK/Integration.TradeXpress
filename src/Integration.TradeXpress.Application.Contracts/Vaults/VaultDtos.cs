using System;
using System.ComponentModel.DataAnnotations;
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
    public int CurrentTransactionCount { get; set; }
}

public class VaultGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;

    // Client validasyonu modelin ÜZERİNDE (agnostic Form, LocalizedDataAnnotationsValidator ile doğrular;
    // GraphDto : GetDto bunları miras alır). Server-input doğrulaması Create/Update DTO'larında kalır.
    // BranchId'ye [Required] KONMAZ: bağlam FK'sı (drill'de in-memory parent'ta boş olabilir; server set eder).
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

/// <summary>
/// Branch grafının kasa DÜĞÜMÜ — yalnız in-memory drill + Branch save'i içindir (kendi app servisi YOK;
/// standalone Vault CRUD ayrı: <see cref="VaultCreateDto"/>/<see cref="VaultUpdateDto"/>).
/// <see cref="VaultGetDto"/>'dan TÜRER → alanlar + validasyon attribute'ları TEK KAYNAK (drill, standalone'la
/// aynı VaultLayout'u ve aynı kuralları kullanır; mapping yok). Graf durumu eklenir: ClientKey + IsDeleted.
/// Durum = <see cref="Volo.Abp.Application.Dtos.EntityDto{TKey}.Id"/> + <see cref="IsDeleted"/>:
/// Id boş → ekle, IsDeleted → sil, aksi → güncelle. (BranchId/BranchCode miras gelir; graf save
/// parent branch.Id'yi kullanır, bunlara dokunmaz.)
/// </summary>
public class VaultGraphDto : VaultGetDto
{
    // Graf düğümü varsayılan AKTİF (eski field default'u koru → tüm `new VaultGraphDto` siteleri aktif gelir;
    // explicit initializer / DB reconstruction ezer). VaultGetDto.IsActive default false olduğundan ctor'da set.
    public VaultGraphDto() => IsActive = true;

    public Guid ClientKey { get; set; } = Guid.NewGuid();
    public bool IsDeleted { get; set; }
}
