using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Commodities;

namespace Integration.TradeXpress.Futures;

public class FutureListRequestDto : ListRequestDto
{
}

public class FutureListDto : FollowingUnitCatalogListDtoBase
{
    public decimal FollowingFactor { get; set; }
}

public class FutureGetDto : FollowingUnitCatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(FutureConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class FutureCreateDto : FollowingUnitCatalogCreateDtoBase
{
    [Required]
    [StringLength(FutureConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class FutureUpdateDto : FollowingUnitCatalogUpdateDtoBase
{
    // Kod DÜZENLENEBİLİR (ürün kuralı 2026-07-04: host CurrencyUnit kayıtları dışında tüm kodlar değiştirilebilir).
    [Required]
    [StringLength(FutureConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(FutureConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, double.MaxValue)]
    public decimal FollowingFactor { get; set; } = 1m;

    [StringLength(FutureConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
