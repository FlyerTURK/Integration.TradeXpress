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
    /// <summary>Yeni katalog kaydı açılır; kod/ad ürünün kendisinden ön-doldurulur.</summary>
    CreateNew = 0,

    /// <summary>Mevcut bir katalog kaydı seçilir; yeni kayıt AÇILMAZ.</summary>
    UseExisting = 1,
}
