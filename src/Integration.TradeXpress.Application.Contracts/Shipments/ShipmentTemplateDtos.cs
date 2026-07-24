using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Shipments;

/// <summary>Şablon ÖZEL adresi (gönderim/iade) — <see cref="Address"/> VO'nun düz yansıması. Ortak
/// <c>AddressFields</c> bileşenine bind için <see cref="IAddressEditModel"/>. Picker İl/İlçe/Mahalle + kodları +
/// id-only köprüleri doldurur; serbest-metin yalnız Line/PostalCode/Title.</summary>
public class ShipmentAddressDto : IAddressEditModel
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

    /// <summary>Opsiyonel bina adı — UBL <c>BuildingName</c>.</summary>
    public string? BuildingName { get; set; }

    /// <summary>Opsiyonel bina numarası — UBL <c>BuildingNumber</c>.</summary>
    public string? BuildingNumber { get; set; }

    /// <summary>Opsiyonel oda/daire — UBL <c>Room</c>.</summary>
    public string? Room { get; set; }

    /// <summary>Opsiyonel kat — UBL <c>Floor</c>.</summary>
    public string? Floor { get; set; }

    /// <summary>Opsiyonel posta kutusu — UBL <c>Postbox</c>.</summary>
    public string? Postbox { get; set; }

    /// <summary>Opsiyonel ek cadde/sokak adı — UBL <c>AdditionalStreetName</c>.</summary>
    public string? AdditionalStreetName { get; set; }
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

    /// <summary>Gönderim adresini sağlayan şube (şube modu) — çekirdek Branch'e id-only referans. <see cref="DispatchAddress"/>
    /// ile tam biri dolu (server invariant zorlar). null → özel-adres modu.</summary>
    Guid? DispatchBranchId { get; }

    /// <summary>Gönderim ÖZEL adresi (özel-adres modu) — null → şube modu. <see cref="DispatchBranchId"/> ile tam biri dolu.</summary>
    ShipmentAddressDto? DispatchAddress { get; }

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

    /// <summary>İade adresi = gönderim ile aynı mı. true → iade şube/adres yok sayılır (efektif = gönderim).</summary>
    bool ReturnSameAsDispatch { get; }

    /// <summary>İade adresini sağlayan şube (iade açık + farklı + şube modu) — çekirdek Branch'e id-only referans.</summary>
    Guid? ReturnBranchId { get; }

    ShipmentAddressDto? ReturnAddress { get; }
    string? ReturnInfo { get; }
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

    /// <summary>Gönderim adresini sağlayan şube (şube modu). null → özel-adres modu (<see cref="DispatchAddress"/> dolu).</summary>
    public Guid? DispatchBranchId { get; set; }

    /// <summary>Gönderim ÖZEL adresi (özel-adres modu). null → şube modu.</summary>
    public ShipmentAddressDto? DispatchAddress { get; set; }

    /// <summary>Gönderim şubesinin denormalize adı — salt görüntü (server doldurur; şube modunda).</summary>
    public string? DispatchBranchName { get; set; }

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

    /// <summary>İade adresi = gönderim ile aynı mı.</summary>
    public bool ReturnSameAsDispatch { get; set; }

    /// <summary>İade adresini sağlayan şube (iade açık + farklı + şube modu). null → özel-adres modu ya da yok.</summary>
    public Guid? ReturnBranchId { get; set; }

    public ShipmentAddressDto? ReturnAddress { get; set; }

    /// <summary>İade şubesinin denormalize adı — salt görüntü (server doldurur; iade şube modunda).</summary>
    public string? ReturnBranchName { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }
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

    /// <summary>Gönderim şubesi (şube modu) — null → özel-adres modu.</summary>
    public Guid? DispatchBranchId { get; set; }

    /// <summary>Gönderim ÖZEL adresi (özel-adres modu) — null → şube modu.</summary>
    public ShipmentAddressDto? DispatchAddress { get; set; }

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

    /// <summary>İade adresi = gönderim ile aynı mı.</summary>
    public bool ReturnSameAsDispatch { get; set; }

    /// <summary>İade şubesi (iade açık + farklı + şube modu).</summary>
    public Guid? ReturnBranchId { get; set; }

    public ShipmentAddressDto? ReturnAddress { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }
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

    /// <summary>Gönderim şubesi (şube modu) — null → özel-adres modu.</summary>
    public Guid? DispatchBranchId { get; set; }

    /// <summary>Gönderim ÖZEL adresi (özel-adres modu) — null → şube modu.</summary>
    public ShipmentAddressDto? DispatchAddress { get; set; }

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

    /// <summary>İade adresi = gönderim ile aynı mı.</summary>
    public bool ReturnSameAsDispatch { get; set; }

    /// <summary>İade şubesi (iade açık + farklı + şube modu).</summary>
    public Guid? ReturnBranchId { get; set; }

    public ShipmentAddressDto? ReturnAddress { get; set; }

    [StringLength(ShipmentTemplateConsts.ReturnInfoMaxLength)]
    public string? ReturnInfo { get; set; }
}

/// <summary>Çekirdek kargo şablonunun bir satış kanalına dağıtımı (deployment) — <b>kanal-agnostik</b> özet satırı.
/// Çekirdek şablon formundaki "Satış Kanalları" drill'i tüketir: çekirdeğe bağlı hangi kanal-şablonunun hangi kanalda
/// bulunduğunu gösterir (K1 köprüsü <c>{Kanal}ShipmentTemplate.ShipmentTemplateId</c> üzerinden). <see cref="SalesChannelType"/>
/// ile kanal ailesi ayrışır (şu an yalnız <see cref="SalesChannelType.TrN11"/>). Salt görüntü (server çözer).</summary>
public class ShipmentTemplateChannelDeploymentDto
{
    /// <summary>Dağıtımın hedef kanal ailesi/türü (N11 için <see cref="SalesChannelType.TrN11"/>).</summary>
    public SalesChannelType SalesChannelType { get; set; }

    /// <summary>Dağıtımın hedef satış kanalı — id-only referans.</summary>
    public Guid SalesChannelId { get; set; }

    /// <summary>Satış kanalının adı — denormalize (salt görüntü; server çözer).</summary>
    public string SalesChannelName { get; set; } = string.Empty;

    /// <summary>Kanal kargo şablonunun id'si (ör. <c>N11ShipmentTemplate.Id</c>).</summary>
    public Guid ChannelTemplateId { get; set; }

    /// <summary>Kanal kargo şablonunun adı — denormalize (salt görüntü).</summary>
    public string ChannelTemplateName { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{SalesChannelName} → {ChannelTemplateName}";
    }
}
