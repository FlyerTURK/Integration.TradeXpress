using System;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Reçete paneli için PARASAL VARYANTLI emtia (Good · Jewelry) varyant seçim DTO'su — emtia×varyant YASSI liste
/// (<see cref="Metals.MetalVariantLookupDto"/>'nun parasal ailelere karşılığı; Metal'in kendi DTO'su işçilik/milyem
/// alanları taşıdığından ayrı kalır). Panel combo'su bunu gösterir, satıra <c>CommodityVariantId</c> yazar.
///
/// <para><b>Neden ("varyantlı her emtia reçetede maden gibi davranır" — 2026-08-15 Hakan kararı):</b> Good'da fiyat
/// VARYANTTADIR (GoodVariantDetail) ve sunucu maliyet motoru seçili varyantın fiyatını zaten okur — UI varyant
/// seçtirmediği için hep ana varyanta düşüyor, satır yanlış fiyatlanabiliyordu. Jewelry'de varyantlar fiyatı
/// PAYLAŞIR (bilinçli kısıt); seçim kimlik/stok için anlamlıdır, fiyatı değiştirmez. Stone VARYANTSIZDIR (2026-08-09
/// kuralı) — bu DTO'yu KULLANMAZ.</para>
///
/// <para>Fiyat alanları: Good'da seçili varyantın <c>GoodVariantDetail</c>'inden; Jewelry'de emtianın kendisinden
/// (her varyant satırına aynı değer). <c>PriceByQuantity</c>/<c>IsQuantity</c> her iki ailede ENTITY seviyesindedir.</para>
/// </summary>
public class CommodityVariantLookupDto
{
    public Guid CommodityId { get; set; }
    public string CommodityCode { get; set; } = string.Empty;
    public string CommodityName { get; set; } = string.Empty;

    public Guid VariantId { get; set; }
    public string VariantCode { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;

    /// <summary>Ana (main) varyant mı — <c>ApplyCommoditySelection</c> "üründen yarat"ta bunu seçer.</summary>
    public bool IsMain { get; set; }

    public bool IsQuantity { get; set; }
    public bool PriceByQuantity { get; set; }

    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    public string DisplayText => $"{CommodityCode} / {VariantCode}";

    public override string ToString()
    {
        return DisplayText;
    }
}
