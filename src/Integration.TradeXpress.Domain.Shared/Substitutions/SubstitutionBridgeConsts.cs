namespace Integration.TradeXpress.Substitutions;

/// <summary>Muadil → kanal StockItem köprüsü (M4) sabitleri — N11 ve Trendyol adaptörleri ORTAK kullanır.</summary>
public static class SubstitutionBridgeConsts
{
    /// <summary>Kanal ürününde kombinasyonları taşıyan özelliğin görünen adı (tr ürün kararı 2026-07-09).
    /// Değer normalize (TitleCase) edilmiş hâliyle persist edilir — "Kombinasyon" zaten TitleCase.</summary>
    public const string CombinationAttributeName = "Kombinasyon";
}
