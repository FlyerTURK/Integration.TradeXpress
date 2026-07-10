namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil türü — grubun hangi emtia ailesini ikame ettiği. Şimdilik yalnız Metal;
/// ileride Mamül vb. genişler (konsept: "tür alanı ileride Mamül vb. genişler").</summary>
public enum SubstitutionType
{
    /// <summary>Maden (adet-hesaplı, standart gramajlı Metal kayıtları — IsQuantity + StableQuantity).</summary>
    Metal = 1,
}
