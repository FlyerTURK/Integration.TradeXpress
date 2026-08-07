namespace Integration.TradeXpress.Products;

/// <summary>
/// Sihirbaz sınıflandırmasında bir ürünün emtiaya nasıl bağlanacağı (2026-08-05 Hakan kararı:
/// <i>"Sorsun ne türlü bir emtia eklenecek diye… Bu karar MANUEL olarak verilecek bir şey olacaktır."</i>).
/// <para>İki mod gereklidir çünkü aileler farklı davranır: Maden'de on bilezik çoğunlukla AYNI "22 Ayar"
/// madenini tüketir (<see cref="UseExisting"/>), Mamül'de her ürün kendi katalog kaydını ister
/// (<see cref="CreateNew"/>). Tek moda zorlamak ya katalogda kopya üretirdi ya da kullanıcıyı her ürün için
/// elle emtia aramaya mecbur bırakırdı.</para>
/// </summary>
public enum ProductCommodityProvisionMode : byte
{
    /// <summary>Yeni katalog kaydı açılır; yalnız kod/ad taşınır, kalan alanlar entity varsayılanına düşer.
    /// <para><b>Metal-bacaklı ailelerde (Metal/Scrap/Future) YASAKTIR</b> — varsayılan milyem (0.995 / 0.570)
    /// makul görünen bir TAHMİNDİR ve sessizce her değerlemeye girer. Oralarda
    /// <see cref="CloneExisting"/> ya da <see cref="UseExisting"/> kullanılır.</para></summary>
    CreateNew = 0,

    /// <summary>Mevcut bir katalog kaydı seçilir; yeni kayıt AÇILMAZ.
    /// <para>Maden'de baskın durum budur: on bilezik aynı "22 Ayar" madenini tüketir.</para></summary>
    UseExisting = 1,

    /// <summary>Mevcut bir kayıt ŞABLON alınıp yeni kod/adla KOPYALANIR (2026-08-06 Hakan isteği).
    /// <para>Milyem, adet-gram katsayısı, işçilik ve fiyat ayarları GERÇEK bir kayıttan devralınır — bu yüzden
    /// metal-bacaklı ailelerde de güvenlidir: uydurulmuş bir sayı yoktur, kullanıcının daha önce doğruladığı
    /// bir kaydın değerleri vardır.</para></summary>
    CloneExisting = 2,
}
