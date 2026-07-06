namespace Integration.TradeXpress.Products;

/// <summary>
/// Türev/devralan reçete satırının <b>taban SWITCH</b>'i — üstüne işlem uygulanacak devralınan maliyet
/// tabanının hangi satırlardan toplandığını belirtir (3b). Yalnız <see cref="RecipeComponentType.Derived"/>
/// satırında anlamlıdır; türev-dışı satırda null.
/// </summary>
public enum RecipeDerivedBaseMode : byte
{
    /// <summary>Genel toplam — o satıra kadarki TÜM üst satırların net toplamı (devreden) taban alınır.</summary>
    AllAbove = 1,

    /// <summary>Belli kalemler — yalnız SEÇİLİ üst satırların maliyet toplamı taban alınır (TagBox seçimi).</summary>
    SelectedLines = 2,
}
