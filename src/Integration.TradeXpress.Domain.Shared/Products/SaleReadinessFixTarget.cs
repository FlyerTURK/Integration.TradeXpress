namespace Integration.TradeXpress.Products;

/// <summary>
/// Issue'un "DÜZELT →" bağlantısının götürdüğü yer (2026-08-19 ürün satışa hazırlık paneli). Sunucu issue'u
/// üretirken hedefi de söyler; UI yalnız eşler (sekme aç / varyant formu aç / kanal ürünü formu aç). Kural UI'da
/// yeniden hesaplanmaz — issue ile hedef birlikte yaşar, ayrışamaz.
/// </summary>
public enum SaleReadinessFixTarget : byte
{
    /// <summary>Gidilecek yer yok (bilgi) ya da düzeltme dışarıda (ör. emtia formu).</summary>
    None = 0,

    /// <summary>Ürün formu → Genel sekmesi (kategori, KDV, varyant modu, stok politikası).</summary>
    GeneralTab = 1,

    /// <summary>Ürün formu → Varyantlar sekmesi (liste).</summary>
    VariantsTab = 2,

    /// <summary>Belirli bir varyantın formu (<c>TargetId</c> = varyant id): satış fiyatı, reçete, durum.</summary>
    VariantForm = 3,

    /// <summary>Ürün formu → Medya sekmesi.</summary>
    MediaTab = 4,

    /// <summary>Ürün formu → Satış Kanalı Ürünleri sekmesi (kanal ürünü ekle).</summary>
    ChannelsTab = 5,

    /// <summary>Belirli bir kanal ürününün formu (<c>TargetId</c> = kanal ürünü id).</summary>
    ChannelProductForm = 6,

    /// <summary>"Satışa Doğrula" aksiyonu (onay eksik/bayat).</summary>
    Verify = 7,
}
