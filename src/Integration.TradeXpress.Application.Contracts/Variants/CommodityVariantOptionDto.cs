using System;

namespace Integration.TradeXpress.Variants;

/// <summary>
/// Fiş satırı panelinin varyant combo'su için hafif seçenek DTO'su — bir emtianın (Good/Jewelry/Stone/Metal) AKTİF
/// varyantları. Fiyat alanları YALNIZ varyant-başı fiyatı olan emtiada (Good → GoodVariantDetail) doludur; diğerlerinde
/// 0/null gelir ve panel <c>VariantsHaveOwnPricing=false</c> iken bunları YOKSAYAR (fiyat emtia seviyesinde kalır).
/// </summary>
public class CommodityVariantOptionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;

    /// <summary>Ana (main) varyant mı — tek-varyantlı emtiada panel VariantId'yi null bırakır; çoklu-da ana varsayılan seçilir.</summary>
    public bool IsMain { get; set; }

    // ── Varyant-başı fiyat (yalnız Good gibi fiyatı varyanta taşınmış emtiada) ──
    public decimal EntryPrice { get; set; }
    public Guid? EntryPriceUnitId { get; set; }
    public decimal ExitPrice { get; set; }
    public Guid? ExitPriceUnitId { get; set; }

    public override string ToString()
    {
        return Code;
    }
}
