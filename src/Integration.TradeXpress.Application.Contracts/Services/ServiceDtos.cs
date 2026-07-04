using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;

namespace Integration.TradeXpress.Services;

/// <summary>Service listesi sorgusu (host kataloğu + tenant'ın kendileri).</summary>
public class ServiceListRequestDto : ListRequestDto
{
}

public class ServiceListDto : CatalogListDtoBase
{
}

public class ServiceGetDto : CatalogGetDtoBase, IHasCode
{
    [Required]
    [StringLength(ServiceConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ServiceCreateDto : CatalogCreateDtoBase
{
    [Required]
    [StringLength(ServiceConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public override string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}

public class ServiceUpdateDto : CatalogUpdateDtoBase
{
    [Required]
    [StringLength(ServiceConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public override string Name { get; set; } = string.Empty;

    [StringLength(ServiceConsts.DescriptionMaxLength)]
    public string? Description { get; set; }
}
