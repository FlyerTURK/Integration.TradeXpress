using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 kargo şablonu istemcisi — SOAP ShipmentService (GetShipmentTemplateList / CreateOrUpdateShipmentTemplate).
/// Kanalın KENDİ kimliğiyle çağrılır (per-satıcı). Model ÇÖZÜLMÜŞ gelir/gider (firma isim+shortName, il code+name,
/// ilçe id+name) — id→isim çözümü AppService'te (DB lookup); client yalnız XML serialize/parse eder.
/// <para><b>Şartlı Kargo (feeCondition):</b> adres elementine gömülü — client push'ta da gönderir, okurken de parse eder
/// (CANLI DOĞRULANDI: CreateOrUpdate feeCondition/feeConditionPrice'ı kabul edip saklıyor; resmî doküman alan tablosu eksikmiş).</para>
/// </summary>
public interface IN11ShipmentTemplateClient
{
    /// <summary>Satıcının N11'deki tüm kargo şablonlarını çeker (içe aktarım için).</summary>
    Task<IReadOnlyList<N11ShipmentTemplateData>> GetTemplateListAsync(string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Şablonu N11'e oluşturur/günceller (templateName kimliğiyle upsert; şartlı kargo dahil).</summary>
    Task CreateOrUpdateAsync(N11ShipmentTemplateData template, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

/// <summary>N11 kargo şablonu — ÇÖZÜLMÜŞ (isim/kod dolu) veri; XML'e birebir serialize edilir / XML'den parse edilir.</summary>
public sealed record N11ShipmentTemplateData(
    string TemplateName,
    byte DeliveryFeeType,
    byte ShipmentMethod,
    bool SpecialDelivery,
    bool CombinedShipmentAllowed,
    bool UseDmallCargo,
    string? ShippingInfo,
    string? ExchangeInfo,
    string? InstallmentInfo,
    string? CargoAccountNo,
    N11ShipmentCompanyRef? ClaimShipmentCompany,
    N11ShipmentAddressData WarehouseAddress,
    N11ShipmentAddressData? ExchangeAddress,
    IReadOnlyList<N11ShipmentCompanyRef> ShipmentCompanies,
    IReadOnlyList<N11ShipmentCityRef> DeliverableCities);

/// <summary>Kargo firması referansı (N11'de id yok → name + shortName). ShortName OPSİYONEL —
/// N11 kısa-kodsuz firma döndürebiliyor (DHL/Asil/Fillo); wire gerçeği olduğu gibi taşınır.</summary>
public sealed record N11ShipmentCompanyRef(string Name, string? ShortName);

/// <summary>İl referansı (code + name).</summary>
public sealed record N11ShipmentCityRef(string Code, string Name);

/// <summary>Şablon adresi (ShipmentSaveAddress) — çözülmüş il/ilçe (code/id + name). Şartlı kargo alanları (N11'de adrese
/// gömülü <c>feeCondition</c> tip + <c>feeConditionPrice</c>/<c>feeConditionUnit</c> değer) hem push'ta gönderilir hem okunur.</summary>
public sealed record N11ShipmentAddressData(
    string? Title,
    string Line,
    string CityCode,
    string CityName,
    string? DistrictId,
    string? DistrictName,
    string? PostalCode,
    decimal? ConditionalShippingThreshold = null,
    N11ConditionalShippingUnit? ConditionalShippingUnit = null);
