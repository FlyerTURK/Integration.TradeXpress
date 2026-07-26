namespace Integration.TradeXpress.Products;

/// <summary>Muadil (Substitution) üründe kombinasyonların varyanta dönüşme biçimi (2026-07-25 Hakan kararı:
/// "kullanıcı tek seçerse tek, çoklu seçerse çoklu; en iyi rank ana varyant, diğerleri müşteriye seçim
/// zenginliği"). Üretim OTOMATİKTİR — "Uygula" butonu yok; ürün kaydında ve maden stoğu değişince
/// (ProductOrchestrationManager) o anki stoğa göre yeniden üretilir.
/// <para><b>Sayısal düzen:</b> Single=0 bilinçli — STATÜKO (bugünkü tek-ana-varyant davranışı); mevcut muadil
/// satırlar migration default'u (0) ile davranış değiştirmez.</para></summary>
public enum SubstitutionVariantMode
{
    /// <summary>Tek — yalnız EN İYİ rank (Rank 1) kombinasyon ana varyantın reçetesi olur (STATÜKO/varsayılan).</summary>
    Single = 0,

    /// <summary>Çoklu — başarılı kombinasyonlar AYRI varyantlar olarak materyalize edilir: Rank 1 = ana,
    /// diğerleri müşteriye seçenek. Kombinasyonlar stoktan türediği için stok değişince yeniden üretilir.</summary>
    Multi = 1,
}
