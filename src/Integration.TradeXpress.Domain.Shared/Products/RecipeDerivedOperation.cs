namespace Integration.TradeXpress.Products;

/// <summary>
/// Türev/devralan reçete satırının devralınan tabana uyguladığı <b>işlem</b> (3b). Net maliyete katkı DAİMA
/// <b>delta</b>'dır (sonuç − taban); satır tam sonucu gösterir. Yalnız <see cref="RecipeComponentType.Derived"/>
/// satırında anlamlıdır; türev-dışı satırda null.
/// </summary>
public enum RecipeDerivedOperation : byte
{
    /// <summary>Ekle — sonuç = taban + operand (operand = ülke birimi cinsinden mutlak tutar). Delta = operand.</summary>
    Add = 1,

    /// <summary>Çarp — sonuç = taban × operand (ör. 1,2 → +%20). Delta = taban×(operand−1).</summary>
    Multiply = 2,

    /// <summary>Yüzde — sonuç = taban × (1 + operand/100), yani tabana %operand EKLER (ör. 10 → +%10; −5 → %5 indirim).
    /// Delta = taban × operand/100.</summary>
    Percent = 3,

    /// <summary>Brütleştir (komisyon/masraf) — sonuç = taban ÷ (1 − operand/100). Satış fiyatından kesilen bir
    /// komisyonu KARŞILAMAK için gereken brüt tutar (ör. %5,1 komisyon → 1000 ÷ 0,949 = 1053,74; komisyon sonrası
    /// eline tam taban kadar geçer). operand ∈ [0, 100). Delta = taban × operand/(100−operand).</summary>
    GrossUp = 4,
}
