using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;

namespace Integration.TradeXpress.Commodities;

// FollowingUnit taşıyan kataloglar (Metal/Scrap/Future) için ara base'ler.
// SADECE her üçünde birebir aynı olan blok burada: FollowingUnitId (+ [Required] — entity'den
// bağımsız, üçünde de aynı) ve Get/List'te FollowingUnitCode. Factor/FollowingFactor TÜREVDE
// kalır: adı (Future: FollowingFactor), Range üst sınırı (Scrap: 1.0) ve default'u entity-özel.

/// <summary>FollowingUnit'li katalog List DTO base'i (Metal/Scrap/Future).</summary>
public abstract class FollowingUnitCatalogListDtoBase : CatalogListDtoBase, IFollowingUnitDto
{
    public Guid FollowingUnitId { get; set; }

    /// <summary>Map sonrası FollowingUnitCatalogAppService tabanınca doldurulur.</summary>
    public string? FollowingUnitCode { get; set; }
}

/// <summary>FollowingUnit'li katalog Get DTO base'i (Metal/Scrap/Future).</summary>
public abstract class FollowingUnitCatalogGetDtoBase : CatalogGetDtoBase, IFollowingUnitDto
{
    [Required]
    public Guid? FollowingUnitId { get; set; }

    /// <summary>Map sonrası FollowingUnitCatalogAppService tabanınca doldurulur.</summary>
    public string? FollowingUnitCode { get; set; }
}

/// <summary>FollowingUnit'li katalog Create DTO base'i (Metal/Scrap/Future).</summary>
public abstract class FollowingUnitCatalogCreateDtoBase : CatalogCreateDtoBase
{
    [Required]
    public Guid? FollowingUnitId { get; set; }
}

/// <summary>FollowingUnit'li katalog Update DTO base'i (Metal/Scrap/Future).</summary>
public abstract class FollowingUnitCatalogUpdateDtoBase : CatalogUpdateDtoBase
{
    [Required]
    public Guid? FollowingUnitId { get; set; }
}
