using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Addressing;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>Şablon adresi (depo/değişim) — <see cref="Address"/> VO'nun düz yansıması; ortak <c>AddressFields</c>
/// bileşenine bind için <see cref="IAddressEditModel"/>. İl/İlçe hem ad hem kod taşır (picker doldurur).</summary>
public class N11ShipmentAddressDto : IAddressEditModel
{
    public string? Title { get; set; }
    public string City { get; set; } = string.Empty;
    public string Line { get; set; } = string.Empty;
    public string? District { get; set; }
    public string? Neighborhood { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "TR";

    /// <summary>Ülke ADI — salt görüntü (adres özetinde kod yerine "Türkiye"). Otoriter alan CountryCode'dur.</summary>
    public string? CountryName { get; set; }

    /// <summary>N11 il kodu (1–81) — <see cref="Integration.TradeXpress.N11Cities.N11CityDto.CityCode"/>.</summary>
    public string? CityCode { get; set; }

    /// <summary>N11 ilçe id'si — <see cref="Integration.TradeXpress.N11Cities.N11DistrictDto.DistrictId"/>.</summary>
    public string? DistrictCode { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya idari-alan (il/eyalet) id'si — picker doldurur (id-only köprü). N11 push OKUMAZ.</summary>
    public Guid? AdministrativeAreaId { get; set; }

    /// <summary>Opsiyonel çekirdek coğrafya yerellik (ilçe) id'si — picker doldurur. N11 push OKUMAZ.</summary>
    public Guid? LocalityId { get; set; }

    /// <summary>Opsiyonel ISO 3166-2 idari-alan kodu (ör. "TR-34") — UBL projeksiyonu için. N11 push OKUMAZ.</summary>
    public string? AdministrativeAreaIsoCode { get; set; }

    /// <summary>Opsiyonel bina adı — UBL <c>BuildingName</c>. N11 push OKUMAZ.</summary>
    public string? BuildingName { get; set; }

    /// <summary>Opsiyonel bina numarası — UBL <c>BuildingNumber</c>. N11 push OKUMAZ.</summary>
    public string? BuildingNumber { get; set; }

    /// <summary>Opsiyonel oda/daire — UBL <c>Room</c>. N11 push OKUMAZ.</summary>
    public string? Room { get; set; }

    /// <summary>Opsiyonel kat — UBL <c>Floor</c>. N11 push OKUMAZ.</summary>
    public string? Floor { get; set; }

    /// <summary>Opsiyonel posta kutusu — UBL <c>Postbox</c>. N11 push OKUMAZ.</summary>
    public string? Postbox { get; set; }

    /// <summary>Opsiyonel ek cadde/sokak adı — UBL <c>AdditionalStreetName</c>. N11 push OKUMAZ.</summary>
    public string? AdditionalStreetName { get; set; }
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

    /// <summary>Şablonun kargo firmaları (N11 kargo firması ExternalId'leri) — seçim/gösterim için düz liste.
    /// Cari bağı için <see cref="Companies"/>'e bakılır (aynı firmalar, cari alanıyla birlikte).</summary>
    public List<string> ShipmentCompanyExternalIds { get; set; } = new();

    /// <summary>Şablonun kargo firmaları (kimlik + aynaıdan okunan ad).</summary>
    public List<N11ShipmentTemplateCompanyDto> Companies { get; set; } = new();

    /// <summary>Şablon N11'de hâlâ var mı. Senkron, N11'den kalkan şablonu silmez → pasifleştirir.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Teslimat yapılan iller (N11 il kodları) — <b>BOŞ OLAMAZ</b>: N11 boş listeyi "hiçbir şehre
    /// teslimat yok" diye kaydeder (tüm iller DEĞİL).</summary>
    public List<string> DeliverableCityCodes { get; set; } = new();
}

/// <summary>Şablonun tek kargo firması satırı — firma kimliği + varsayılan cari alt hesap (+ gösterim adları).</summary>
public class N11ShipmentTemplateCompanyDto
{
    /// <summary>N11 kargo firması kimliği (<c>N11ShipmentCompany.ExternalId</c>).</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Firma adı — host-global aynadan çözülür (gösterim; persist EDİLMEZ).</summary>
    public string Name { get; set; } = string.Empty;

    public override string ToString()
    {
        return $"{ExternalId} ({Name})";
    }
}

/// <summary>Create/Update ortak düzenlenebilir alanları — AppService tek <c>ApplyInput</c> ile uygular (DRY).
/// Şartlı kargo dahil (feeCondition push ile yazılabilir — canlı doğrulandı).</summary>
public interface IN11ShipmentTemplateInput
{
    /// <summary>Çekirdek ERP kargo şablonu referansı (K1 köprüsü; id-only, opsiyonel).</summary>

    string TemplateName { get; }
    N11DeliveryFeeType DeliveryFeeType { get; }
    N11ShipmentMethod ShipmentMethod { get; }
    bool SpecialDelivery { get; }
    bool CombinedShipmentAllowed { get; }
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

    /// <summary>Yerel şablonları N11 ile HİZALAR: N11'deki şablonları çekip yerelde upsert eder (isim/kod → id
    /// ters-çözümü) + N11'de artık olmayan şablonları PASİFLEŞTİRİR (silmez — kullanıcının kurduğu cari bağları
    /// yaşasın). Yeni gelen kargo firmaları cariyi kardeş şablonlardan devralır; devralamayan ÖKSÜZ kalır.
    /// Değişen (yeni+güncellenen) sayısını döner.</summary>
    Task<int> SyncAsync(Guid salesChannelId);

}
