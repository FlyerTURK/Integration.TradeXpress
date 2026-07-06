namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satırının bileşen türü — bir <c>ProductVariantRecipeLine</c>'ın maliyete hangi biçimde
/// katkıda bulunduğunu belirtir. Design-time maliyet kompozitörüdür; LEDGER'A YAZMAZ.
///
/// <para>3a kapsamı: <see cref="CatalogCommodity"/> (Metal/Scrap/Future/Jewelry/Stone — aile
/// <c>CommodityProcessType</c> ile ayrılır), <see cref="Service"/> (sabit tutar@birim, opsiyonel
/// hizmet katalog referansı) ve <see cref="ManualCost"/> (serbest tutar@birim). Türev/devralan
/// (<c>Derived</c>) ve iç-içe mamul (<c>ProductComponent</c>) satır türleri SONRAKİ adımlarda (3b/3c).</para>
/// </summary>
public enum RecipeComponentType : byte
{
    /// <summary>Katalog emtiası — Metal/Scrap/Future (metal-bacaklı, milyem×miktar) ya da Jewelry/Stone
    /// (parasal, giriş fiyatı×miktar). Hangi aile olduğu <c>CommodityProcessType</c> ile taşınır.</summary>
    CatalogCommodity = 1,

    /// <summary>Hizmet — kullanıcının girdiği SABİT tutar@birim (opsiyonel hizmet katalog referansı). Marj
    /// devralış mekanizması 3d'de.</summary>
    Service = 2,

    /// <summary>Manuel maliyet — katalog referansı olmayan serbest tutar@birim.</summary>
    ManualCost = 3,
}
