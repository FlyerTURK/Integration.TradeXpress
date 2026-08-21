namespace Integration.TradeXpress.Products;

/// <summary>Ürün düzeyi varyant üretim tercihi (Dilim-3, 2026-07-24 onaylı tasarım).
/// <para><b>Kullanım senaryosu:</b> "Yeni-Eski Karışık Ziynet Sepeti" ürünü muadil grubunun TÜM varyantlarıyla
/// kombinasyon kurarken; "Yeni Tarihli Ziynet Sepeti" ürünü override ağacında yalnız yeni tarihli varyantları
/// bırakarak kombinasyonu daraltır (grup ayarı ezilir — resolver zinciri: override ?? included ?? ana).</para>
/// <para><b>Sayısal düzen:</b> MultiVariant=0 bilinçli — varsayılan/statüko mod; mevcut satırlar migration
/// default'u (0) ile davranış değiştirmeden MultiVariant kalır (default(enum) = statüko).</para></summary>
public enum ProductVariantMode
{
    /// <summary>Çoklu varyant (STATÜKO/varsayılan) — nitelik×değer kartezyeninden varyant üretilir.</summary>
    MultiVariant = 0,

    /// <summary>Tek varyant — nitelik-tabanlı üretim KAPALI; ürün tek ana varyantla yaşar
    /// (sunucuda <c>ProductAppService.BuildEffectiveAttributeGraphAsync</c> nitelik grafını boşaltır,
    /// synchronizer tek ana varyanta indirir).</summary>
    SingleVariant = 1,

    /// <summary>Muadil (paket) — ürün tek ana varyanttır; reçetesi muadil grubu kombinasyon hesabından
    /// üretilir (grup + hedef miktar + tolerans + opsiyonel varyant override ağacı).</summary>
    Substitution = 2,

    /// <summary>
    /// Katalogdan (varyant şablonu) — nitelikler elle girilmez, şirketin şablon katalogundan seçilir; seçilen
    /// şablonun grupları/değerleri ürünün nitelik grafına katılır ve varyantlar oradan üretilir.
    /// <para>Üretim mekaniği <see cref="MultiVariant"/> ile AYNIDIR (nitelik×değer kartezyeni) — ayrı bir mod
    /// olmasının sebebi NİTELİKLERİN KAYNAĞINI seçtirmek: kullanıcı "elle mi gireceğim, katalogdan mı
    /// alacağım" kararını en baştan, varyant modu combo'sunda verir. Öncesinde şablon seçimi nitelik
    /// popup'ının içinde iki tık derinde kalıyordu ve pratikte bulunamıyordu (2026-07-27 Hakan kararı).</para>
    /// </summary>
    FromCatalog = 3,
}
