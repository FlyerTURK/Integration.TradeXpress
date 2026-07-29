using System;
using System.Collections.Generic;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.MarketplaceShipmentTariffs;

/// <summary>
/// Yürürlükteki bir kargo tarifesinin liste satırı (taşıyıcı başına). Tarife pazaryerinin YAYIMLADIĞI
/// listedir — kullanıcı düzenlemez, yalnız görüntüler; bu yüzden Create/Update DTO'su YOK.
/// </summary>
public class MarketplaceShipmentTariffDto
{
    public Guid Id { get; set; }

    public SalesChannelType Channel { get; set; }

    public string CarrierCode { get; set; } = string.Empty;

    public string CarrierName { get; set; } = string.Empty;

    /// <summary>Kümülatif mi parça başı mı ücretlendiriliyor (N11'de yalnız PTT parça başı).</summary>
    public ShipmentChargeBasis ChargeBasis { get; set; }

    /// <summary>Tablo son satırının üstü için desi başına artış.</summary>
    public decimal OverflowIncrementAmount { get; set; }

    public decimal VatRate { get; set; }

    public decimal PostalServiceFeeRate { get; set; }

    /// <summary>Taşıyıcıya özel sabit ek bedel (N11'de yalnız Yurtiçi'nin SMS ücreti).</summary>
    public decimal ExtraFeeAmount { get; set; }

    public decimal FailedDeliveryRate { get; set; }

    /// <summary><c>null</c> = kaynaktaki yazım belirsiz olduğu için bilinçli boş (bkz. entity notu).</summary>
    public decimal? HeavyCargoAmount { get; set; }

    public DateTime EffectiveFrom { get; set; }

    /// <summary><c>null</c> = hâlâ yürürlükte.</summary>
    public DateTime? EffectiveTo { get; set; }

    public string SourceVersion { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    /// <summary>Şartlı bareme kadar geçerli üst desi sınırı; <c>null</c> = barem yok.</summary>
    public int? ConditionalMaxDesi { get; set; }

    public override string ToString()
    {
        return $"{Channel}/{CarrierCode} ({SourceVersion})";
    }
}

/// <summary>Tarifenin tam görünümü — desi tablosu + şartlı barem dahil (detay ekranı).</summary>
public class MarketplaceShipmentTariffDetailDto : MarketplaceShipmentTariffDto
{
    public List<MarketplaceShipmentTariffRateDto> Rates { get; set; } = new();

    public List<MarketplaceShipmentConditionalRateDto> ConditionalRates { get; set; } = new();
}

/// <summary>Tek desi basamağının çıplak fiyatı (KDV/PHB hariç).</summary>
public class MarketplaceShipmentTariffRateDto
{
    /// <summary>0 = pazaryerinin "Dosya" satırı.</summary>
    public int Desi { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>Şartlı barem dilimi (sepet aralığı → sabit ücret).</summary>
public class MarketplaceShipmentConditionalRateDto
{
    public decimal BasketFrom { get; set; }

    public decimal? BasketTo { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>Liste filtresi — kanal ve "yalnız yürürlüktekiler".</summary>
public class MarketplaceShipmentTariffListInput
{
    public SalesChannelType? Channel { get; set; }

    /// <summary><c>true</c> (varsayılan) = yalnız yürürlükteki sürümler; <c>false</c> = geçmiş sürümler dahil.</summary>
    public bool OnlyEffective { get; set; } = true;
}
