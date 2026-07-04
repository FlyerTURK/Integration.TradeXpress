using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.Commodities;

namespace Integration.TradeXpress.Scraps;

public class ScrapListRequestDto : ListRequestDto
{
}

public class ScrapListDto : FollowingUnitCatalogListDtoBase
{
    public decimal Factor { get; set; }
    public bool FactorChange { get; set; }
}

public class ScrapGetDto : FollowingUnitCatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(ScrapConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, 1.0)]
    public decimal Factor { get; set; } = ScrapConsts.DefaultFactor;

    public bool FactorChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ScrapCreateDto : FollowingUnitCatalogCreateDtoBase
{
    [Required]
    [StringLength(ScrapConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, 1.0)]
    public decimal Factor { get; set; } = ScrapConsts.DefaultFactor;

    public bool FactorChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ScrapUpdateDto : FollowingUnitCatalogUpdateDtoBase
{
    [Required]
    [StringLength(ScrapConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [Range(0.0000001, 1.0)]
    public decimal Factor { get; set; } = ScrapConsts.DefaultFactor;

    public bool FactorChange { get; set; } = true;

    [StringLength(ScrapConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
