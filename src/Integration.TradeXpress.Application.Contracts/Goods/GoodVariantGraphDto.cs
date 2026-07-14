using System;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Good varyantının GRAF DTO'su — jenerik <see cref="EntityVariantGraphDto"/> (çekirdek: Code/Ad/Barkod/Stok adedi/…)
/// + Good-ÖZEL fiyat/stok UZANTISI. Perakende fiyat/stok VARYANT seviyesinde (GoodVariantDetail tablosu).
/// <c>EntityVariantsPanel&lt;GoodVariantGraphDto&gt;</c>'ın ExtraFields slot'unda bu alanlar bind edilir; GoodAppService
/// jenerik çekirdeği kaydettikten sonra bu alanları GoodVariantDetail'e (EntityVariantId ile) saklar/yükler.
/// </summary>
public class GoodVariantGraphDto : EntityVariantGraphDto
{
    // ── Stok limitleri (varyant-başı; opsiyonel). Stok BİRİMİ ana mamülde kalır (varyant graf'ında YOK). ──
    public decimal? MinQuantity { get; set; }
    public decimal? MaxQuantity { get; set; }

    // ── Fiyat (varyant-başı) ──
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    /// <summary>Alış KDV DAHİL mi — VARSAYILAN dahil (true).</summary>
    public bool EntryPriceTaxIncluded { get; set; } = true;

    public MarginType MarginType { get; set; } = MarginType.Multiply;
    public decimal MarginValue { get; set; } = 1m;

    /// <summary>Türetilmiş satış (SALT-OKUMA; Margin.Apply(EntryPrice)) — sunucu doldurur, save yoksayar.</summary>
    public decimal ExitPrice { get; set; }
    /// <summary>Satış para birimi — YALNIZ Sabit Fiyat (FinalPrice) marjında bağımsız; diğerlerinde = alış birimi.</summary>
    public Guid? ExitPriceUnitId { get; set; }
    /// <summary>Satış KDV DAHİL mi — VARSAYILAN dahil (true).</summary>
    public bool ExitPriceTaxIncluded { get; set; } = true;
}
