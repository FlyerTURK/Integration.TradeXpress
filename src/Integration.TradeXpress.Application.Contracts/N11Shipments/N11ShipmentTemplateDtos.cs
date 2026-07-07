using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>Şablon adresi (depo/değişim) — <see cref="Integration.Framework.Addressing.Address"/> VO'nun düz yansıması.
/// İl/İlçe hem ad hem kod taşır.</summary>
public class N11ShipmentAddressDto
{
    public string? Title { get; set; }
    public string City { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Neighborhood { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    /// <summary>N11 il kodu (1–81) — <see cref="Integration.TradeXpress.N11Cities.N11CityDto.CityCode"/>.</summary>
    public string? CityCode { get; set; }

    /// <summary>N11 ilçe id'si — <see cref="Integration.TradeXpress.N11Cities.N11DistrictDto.DistrictId"/>.</summary>
    public string? DistrictCode { get; set; }
}

/// <summary>N11 kargo şablonu — tam okuma modeli (edit formu + tekil görüntü).</summary>
public class N11ShipmentTemplateDto
{
    public Guid Id { get; set; }
    public Guid SalesChannelId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public N11DeliveryFeeType DeliveryFeeType { get; set; }
    public N11ShipmentMethod ShipmentMethod { get; set; }
    public bool SpecialDelivery { get; set; }
    public bool CombinedShipmentAllowed { get; set; }
    public bool UseDmallCargo { get; set; }
    public string? ShippingInfo { get; set; }
    public string? ExchangeInfo { get; set; }
    public string? InstallmentInfo { get; set; }
    public string? CargoAccountNo { get; set; }

    /// <summary>İade/talep kargosu firması (N11 kargo firması ExternalId; opsiyonel).</summary>
    public string? ClaimShipmentCompanyExternalId { get; set; }

    /// <summary>Şartlı kargo eşiği (bu tutar/adet üzeri ücretsiz). Push ile yazılabilir (canlı doğrulandı; feeCondition/feeConditionPrice).</summary>
    public decimal? ConditionalShippingThreshold { get; set; }

    /// <summary>Şartlı kargo eşiğinin birimi (TL/adet).</summary>
    public N11ConditionalShippingUnit ConditionalShippingUnit { get; set; } = N11ConditionalShippingUnit.Amount;

    public N11ShipmentAddressDto WarehouseAddress { get; set; } = new();
    public N11ShipmentAddressDto? ExchangeAddress { get; set; }

    /// <summary>Şablonun kargo firmaları (N11 kargo firması ExternalId'leri).</summary>
    public List<string> ShipmentCompanyExternalIds { get; set; } = new();

    /// <summary>Teslimat yapılan iller (N11 il kodları); boş = tüm iller.</summary>
    public List<string> DeliverableCityCodes { get; set; } = new();
}

/// <summary>Create/Update ortak düzenlenebilir alanları — AppService tek <c>ApplyInput</c> ile uygular (DRY).
/// Şartlı kargo dahil (feeCondition push ile yazılabilir — canlı doğrulandı).</summary>
public interface IN11ShipmentTemplateInput
{
    string TemplateName { get; }
    N11DeliveryFeeType DeliveryFeeType { get; }
    N11ShipmentMethod ShipmentMethod { get; }
    bool SpecialDelivery { get; }
    bool CombinedShipmentAllowed { get; }
    bool UseDmallCargo { get; }
    string? ShippingInfo { get; }
    string? ExchangeInfo { get; }
    string? InstallmentInfo { get; }
    string? CargoAccountNo { get; }
    string? ClaimShipmentCompanyExternalId { get; }
    decimal? ConditionalShippingThreshold { get; }
    N11ConditionalShippingUnit ConditionalShippingUnit { get; }
    N11ShipmentAddressDto WarehouseAddress { get; }
    N11ShipmentAddressDto? ExchangeAddress { get; }
    List<string> ShipmentCompanyExternalIds { get; }
    List<string> DeliverableCityCodes { get; }
}

/// <summary>Şablon oluşturma girdisi — kanal (<see cref="SalesChannelId"/>) client'tan gelir; şirket sunucuda zorlanır.</summary>
public class N11ShipmentTemplateCreateDto : IN11ShipmentTemplateInput
{
    public Guid SalesChannelId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public N11DeliveryFeeType DeliveryFeeType { get; set; }
    public N11ShipmentMethod ShipmentMethod { get; set; }
    public bool SpecialDelivery { get; set; }
    public bool CombinedShipmentAllowed { get; set; }
    public bool UseDmallCargo { get; set; }
    public string? ShippingInfo { get; set; }
    public string? ExchangeInfo { get; set; }
    public string? InstallmentInfo { get; set; }
    public string? CargoAccountNo { get; set; }
    public string? ClaimShipmentCompanyExternalId { get; set; }
    public decimal? ConditionalShippingThreshold { get; set; }
    public N11ConditionalShippingUnit ConditionalShippingUnit { get; set; } = N11ConditionalShippingUnit.Amount;
    public N11ShipmentAddressDto WarehouseAddress { get; set; } = new();
    public N11ShipmentAddressDto? ExchangeAddress { get; set; }
    public List<string> ShipmentCompanyExternalIds { get; set; } = new();
    public List<string> DeliverableCityCodes { get; set; } = new();
}

/// <summary>Şablon güncelleme girdisi — kanal set-once (route'taki id kimliktir; kanal değişmez).</summary>
public class N11ShipmentTemplateUpdateDto : IN11ShipmentTemplateInput
{
    public string TemplateName { get; set; } = string.Empty;
    public N11DeliveryFeeType DeliveryFeeType { get; set; }
    public N11ShipmentMethod ShipmentMethod { get; set; }
    public bool SpecialDelivery { get; set; }
    public bool CombinedShipmentAllowed { get; set; }
    public bool UseDmallCargo { get; set; }
    public string? ShippingInfo { get; set; }
    public string? ExchangeInfo { get; set; }
    public string? InstallmentInfo { get; set; }
    public string? CargoAccountNo { get; set; }
    public string? ClaimShipmentCompanyExternalId { get; set; }
    public decimal? ConditionalShippingThreshold { get; set; }
    public N11ConditionalShippingUnit ConditionalShippingUnit { get; set; } = N11ConditionalShippingUnit.Amount;
    public N11ShipmentAddressDto WarehouseAddress { get; set; } = new();
    public N11ShipmentAddressDto? ExchangeAddress { get; set; }
    public List<string> ShipmentCompanyExternalIds { get; set; } = new();
    public List<string> DeliverableCityCodes { get; set; } = new();
}

/// <summary>
/// N11 kargo şablonu CRUD — <b>company-owned + per-tenant</b>, kanala bağlı. Şablon bizde tutulur + N11'e push edilir
/// (kanalın KENDİ kimliğiyle). Kendi şablonlarımızı tasarlayabiliriz (oluştur/güncelle/push). <b>Şartlı Kargo istisnası:</b>
/// Şartlı kargo (feeCondition) dahil tüm alanlar push edilir (canlı doğrulandı). N11'de silme yok →
/// <see cref="DeleteAsync"/> yalnız yereli siler.
/// </summary>
public interface IN11ShipmentTemplateAppService : IApplicationService
{
    /// <summary>Bir kanalın şablonları — tam DTO (drill Items'ı doğrudan düzenler; kanal başına az kayıt).</summary>
    Task<List<N11ShipmentTemplateDto>> GetListAsync(Guid salesChannelId);

    Task<N11ShipmentTemplateDto> GetAsync(Guid id);

    Task<N11ShipmentTemplateDto> CreateAsync(N11ShipmentTemplateCreateDto input);

    Task<N11ShipmentTemplateDto> UpdateAsync(Guid id, N11ShipmentTemplateUpdateDto input);

    /// <summary>Yalnız yerel siler (N11'de silme operasyonu yok).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Şablonu N11'e oluşturur/günceller (kanalın kimliğiyle; şartlı kargo dahil).</summary>
    Task PushAsync(Guid id);

    /// <summary>N11'deki tüm şablonları çekip yerelde upsert eder (isim/kod → id ters-çözümü). Yeni+güncellenen sayısını döner.</summary>
    Task<int> ImportAsync(Guid salesChannelId);
}
