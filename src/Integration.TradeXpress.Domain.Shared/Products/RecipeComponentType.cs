namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satırının bileşen türü — bir <c>ProductVariantRecipeLine</c>'ın maliyete hangi biçimde
/// katkıda bulunduğunu belirtir. Design-time maliyet kompozitörüdür; LEDGER'A YAZMAZ.
///
/// <para>Reçete satırı iki tür: <see cref="CatalogCommodity"/> (fiziki katalog — Metal/Scrap/Future/Jewelry/Stone;
/// kendi gerçek maliyetini ekler) ve <see cref="Service"/> (hizmet — devralınan taban üstüne türevsel bedel;
/// komisyon/sigorta/kargo gibi). Türev mekaniği (taban SWITCH + işlem) <b>Hizmet satırında PİLOT</b> olarak yaşar;
/// ileride farklı kullanımlar (ör. Vadeli) için genişleyecek — o gün genellenecek (bugün YAGNI).</para>
/// </summary>
public enum RecipeComponentType : byte
{
    /// <summary>Katalog emtiası — Metal/Scrap/Future (milyem×miktar) ya da Jewelry/Stone
    /// (parasal, giriş fiyatı×miktar). Hangi aile olduğu <c>CommodityProcessType</c> ile taşınır.</summary>
    CatalogCommodity = 1,

    /// <summary>Hizmet — bir hizmet referansı (etiket; katalog entity'sine dokunulmaz) + devralınan taban
    /// (<see cref="RecipeDerivedBaseMode"/>: tüm üst satırlar ya da seçili kalemler) üstüne türevsel bedel
    /// (<see cref="RecipeDerivedOperation"/>: yüzde/brütleştir/…). Satır maliyeti = uygulanan bedel (fee); net'e
    /// bu eklenir. Yalnız kendinden ÖNCEKİ satırları referanslar → döngüsüz + deterministik.</summary>
    Service = 2,
}
