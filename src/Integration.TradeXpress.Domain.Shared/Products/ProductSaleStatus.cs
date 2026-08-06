namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürünün/varyantın PAZARYERİNDE SATILABİLİRLİK durumu (2026-08-05 Hakan kararı).
///
/// <para><b><c>IsActive</c> ile karıştırma:</b> ikisi FARKLI soruyu cevaplar ve yan yana yaşarlar.
/// <c>IsActive</c> = "bu kayıt sistemde kullanılabilir mi" (listelerde çıkar mı, seçilebilir mi) — kod
/// tabanının her yerinde yerleşik konvansiyon. <b>Bu statü</b> = "pazaryerinde satışa sunulabilir mi".
/// Bir varyant aktif olup (ERP'de kullanılıyor, reçetelerde geçiyor) satışa hazır olmayabilir.
/// <b>Etkin satılabilirlik = <c>IsActive</c> VE <see cref="Ready"/>.</b></para>
///
/// <para><b>YÖN KURALI (kritik):</b> sistem <see cref="Ready"/> → <see cref="Suspended"/> yapabilir, ama
/// <see cref="Suspended"/> → <see cref="Ready"/> <b>ASLA</b>. Geri dönüş yalnız insandan geçer. Aksi halde
/// bozulan şey düzelince ürün kendiliğinden satışa döner ve kimse reçeteye bakmamış olur —
/// "reddetme/onaylama zevkini bana bırak" ilkesinin doğrudan karşılığı.</para>
/// </summary>
public enum ProductSaleStatus : byte
{
    /// <summary>Hiç onaylanmadı — reçete eksik ya da kararsız. Başlangıç durumu; satışa ÇIKMAZ.
    /// <i>Fail-closed: yeni/dokunulmamış kayıt satılabilir sayılmaz.</i></summary>
    Draft = 0,

    /// <summary>Doğrulandı, satılabilir. Bu duruma <b>yalnız insan</b> geçirebilir (doğrulama anı).</summary>
    Ready = 1,

    /// <summary>Onaylıydı ama bir şey bozuldu: emtia silindi/pasifleşti, reçete değişti, fiyat çözülemiyor.
    /// Bu duruma <b>yalnız sistem</b> geçirir; çıkışı yalnız insan yapar.</summary>
    Suspended = 2,

    /// <summary>Kullanıcı bilinçli olarak satıştan çekti. Sistem bunu <see cref="Suspended"/>'a
    /// yeniden sınıflandırmaz — kullanıcının kararı kullanıcınındır.</summary>
    Closed = 3,
}
