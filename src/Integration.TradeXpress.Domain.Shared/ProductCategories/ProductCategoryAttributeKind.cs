namespace Integration.TradeXpress.ProductCategories;

/// <summary>
/// Kategori niteliğinin CİNSİ — ürüne nasıl yansıyacağını belirler. Bu ayrım kozmetik değildir; iki cinsin
/// yayılma yolu ve risk profili tamamen farklıdır (2026-07-27 tasarımı):
/// </summary>
public enum ProductCategoryAttributeKind : byte
{
    /// <summary>
    /// SPESİFİKASYON ("Ayar: 14K", "Materyal: Altın") — ürüne KOPYALANMAZ, kategoriden CANLI okunur ve
    /// pazaryeri push'unda kanal kategorisinin niteliklerini ÖN-DOLDURUR. Kategoride düzeltilen bir değer
    /// aynı anda tüm ürünlere yansır; varyant üretimine hiç girmediği için bu tamamen güvenlidir.
    /// </summary>
    Specification = 1,

    /// <summary>
    /// VARYANT EKSENİ ("Renk", "Beden") — ürünün nitelik grafına EKLEMELİ yansır (varyant kartezyenine girer).
    /// <para><b>Neden canlı değil:</b> varyant senkronizasyonu, hedef kartezyende bulunmayan kombinasyonları
    /// SİLER. Canlı bağlansaydı kategoriden tek bir değer çıkarmak, o kategorideki TÜM ürünlerin varyantlarını
    /// ve onlara asılı fiyat/stok/görsel/kanal SKU bağlarını geri dönülemez şekilde silerdi. Bu yüzden ekleme
    /// yayılır, ÇIKARMA ASLA otomatik yayılmaz.</para>
    /// </summary>
    VariantAxis = 2,
}
