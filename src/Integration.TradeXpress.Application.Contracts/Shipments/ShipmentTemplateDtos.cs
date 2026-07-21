using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Shipments;

/// <summary>Şablon adresi (menşei/iade) — <see cref="Integration.Framework.Addressing.Address"/> VO'nun düz yansıması.
/// İl/İlçe hem ad hem opsiyonel yapısal kod taşır.</summary>
public class ShipmentAddressDto
{
    public string? Title { get; set; }

    public string City { get; set; } = string.Empty;

    public string Line { get; set; } = string.Empty;

    public string? District { get; set; }
    public string? Neighborhood { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    /// <summary>Opsiyonel yapısal il kodu (plaka / kanal il kodu).</summary>
    public string? CityCode { get; set; }

    /// <summary>Opsiyonel yapısal ilçe kodu.</summary>
    public string? DistrictCode { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya idari-alan (il/eyalet) id'si — picker doldurur (id-only köprü).</summary>
    public Guid? AdministrativeAreaId { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya yerellik (ilçe) id'si.</summary>
    public Guid? LocalityId { get; set; }

    /// <summary>Opsiyonel ISO 3166-2 idari-alan kodu (ör. "TR-34") — UBL projeksiyonu için.</summary>
    public string? AdministrativeAreaIsoCode { get; set; }
}

/// <summary>Kargo şablonu liste sorgusu (per-tenant, company-owned). Merkezi <see cref="ListRequestDto"/> standardı.</summary>
public class ShipmentTemplateListRequestDto : ListRequestDto
{
}

public class ShipmentTemplateListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ShipmentFeeModel FeeModel { get; set; }
    public string? CarrierName { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Create/Update ortak düzenlenebilir alanları — AppService tek yerden uygular (DRY).</summary>
public interface IShipmentTemplateInput
{
    string Code { get; }
    string Name { get; }
    string? Description { get; }
    ShipmentAddressDto OriginAddress { get; }
    int ProcessingDaysMin { get; }
    int ProcessingDaysMax { get; }
    ShipmentFeeModel FeeModel { get; }
    decimal? ConditionalThreshold { get; }
    ShipmentConditionalUnit? ConditionalUnit { get; }
    int? DeliveryDaysMin { get; }
    int? DeliveryDaysMax { get; }

    /// <summary>Kargo firması — çekirdek <see cref="Carrier"/> kataloğuna id-only referans (opsiyonel; SSOT).
    /// Snapshot <c>CarrierName</c> yazımda server'da çözülen firma adından türetilir (client adı yetkili değil).</summary>
    Guid? CarrierId { get; }
    bool ReturnAccepted { get; }
    ShipmentAddressDto? ReturnAddress { get; }
    string? ReturnInfo { get; }
    int? MaxPurchaseQuantity { get; }
}

public class ShipmentTemplateGetDto : EntityDto<Guid>, IGetDto<Guid>, IHasCode
{
    [Required]
    [StringLength(ShipmentTemplateConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ShipmentTemplateConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ShipmentTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public ShipmentAddressDto OriginAddress { get; set; } = new();

    public int ProcessingDaysMin { get; set; } = 1;
    public int ProcessingDaysMax { get; set; } = 1;

    public ShipmentFeeModel FeeModel { get; set; } = ShipmentFeeModel.Free;
    public decimal? ConditionalThreshold { get; set; }
    public ShipmentConditionalUnit? ConditionalUnit { get; set; }

    public int? DeliveryDaysMin { get; set; }
    public int? DeliveryDaysMax { get; set; }

    /// <summary>Kargo firması — çekirdek Carrier kataloğuna id-only referans (opsiyonel; SSOT). Form picker bunu bağlar.</summary>
    public Guid? CarrierId { get; set; }

    [StringLength(ShipmentTemplateConsts.CarrierNameMaxLength)]
    public string? CarrierName { get; set; }

    public bool ReturnAccepted { get; set; }
    public ShipmentAddressDto? ReturnAddress { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }

    public int? MaxPurchaseQuantity { get; set; }
}

public class ShipmentTemplateCreateDto : IShipmentTemplateInput, ICreateDto
{
    [Required]
    [StringLength(ShipmentTemplateConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ShipmentTemplateConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ShipmentTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public ShipmentAddressDto OriginAddress { get; set; } = new();

    public int ProcessingDaysMin { get; set; } = 1;
    public int ProcessingDaysMax { get; set; } = 1;

    public ShipmentFeeModel FeeModel { get; set; } = ShipmentFeeModel.Free;
    public decimal? ConditionalThreshold { get; set; }
    public ShipmentConditionalUnit? ConditionalUnit { get; set; }

    public int? DeliveryDaysMin { get; set; }
    public int? DeliveryDaysMax { get; set; }

    /// <summary>Kargo firması — çekirdek Carrier kataloğuna id-only referans (opsiyonel; SSOT). Form picker bunu bağlar.</summary>
    public Guid? CarrierId { get; set; }

    [StringLength(ShipmentTemplateConsts.CarrierNameMaxLength)]
    public string? CarrierName { get; set; }

    public bool ReturnAccepted { get; set; }
    public ShipmentAddressDto? ReturnAddress { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }

    public int? MaxPurchaseQuantity { get; set; }
}

public class ShipmentTemplateUpdateDto : IShipmentTemplateInput, IUpdateDto
{
    [Required]
    [StringLength(ShipmentTemplateConsts.CodeMaxLength, MinimumLength = EntityFieldConsts.CodeMinLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(ShipmentTemplateConsts.NameMaxLength, MinimumLength = EntityFieldConsts.NameMinLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ShipmentTemplateConsts.DescriptionMaxLength)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public ShipmentAddressDto OriginAddress { get; set; } = new();

    public int ProcessingDaysMin { get; set; } = 1;
    public int ProcessingDaysMax { get; set; } = 1;

    public ShipmentFeeModel FeeModel { get; set; } = ShipmentFeeModel.Free;
    public decimal? ConditionalThreshold { get; set; }
    public ShipmentConditionalUnit? ConditionalUnit { get; set; }

    public int? DeliveryDaysMin { get; set; }
    public int? DeliveryDaysMax { get; set; }

    /// <summary>Kargo firması — çekirdek Carrier kataloğuna id-only referans (opsiyonel; SSOT). Form picker bunu bağlar.</summary>
    public Guid? CarrierId { get; set; }

    [StringLength(ShipmentTemplateConsts.CarrierNameMaxLength)]
    public string? CarrierName { get; set; }

    public bool ReturnAccepted { get; set; }
    public ShipmentAddressDto? ReturnAddress { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }

    public int? MaxPurchaseQuantity { get; set; }
}
