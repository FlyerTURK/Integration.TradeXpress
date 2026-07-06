namespace Integration.TradeXpress.Products;

/// <summary>
/// <c>ProductVariantRecipeLine</c> alan sınırları + decimal precision (EF Core <c>HasPrecision</c>).
/// Miktar/faktör → N5 (milyem/fiyat paritesi), tutar → N2 (VoucherConsts / FinancialRounding ile hizalı).
/// </summary>
public static class ProductRecipeConsts
{
    // ── decimal precision (EF Core HasPrecision) — VoucherConsts / FinancialRounding paritesi ──
    public const int AmountPrecision = 18;
    public const int AmountScale     = 2;   // tutar/gram (N2)
    public const int FactorPrecision = 18;
    public const int FactorScale     = 5;   // adet/faktör/milyem (N5)

    public const int DescriptionMaxLength = 512;

    // ── türev/devralan satır (3b) ──
    /// <summary>Türev satırın seçili-kaynak satır Id'lerinin '|'-join CSV snapshot'ı için kolon uzunluğu.
    /// ~108 referansa yeter (36 karakter Guid + ayraç); pratikte üst-satır sayısı çok altında.</summary>
    public const int DerivedSourceLineIdsMaxLength = 4000;

    /// <summary>GrossUp (brütleştirme) operand'ının ÜST sınırı (dışlayıcı) — payda 1−operand/100 ≤ 0 olamaz
    /// (sıfıra/negatife bölme). operand ∈ [0, 100).</summary>
    public const decimal GrossUpOperandExclusiveMax = 100m;
}
