namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// N11'in RESMÎ hata metinleri — <b>birebir</b>, `.claude/research/n11-catalog/api-hata-mesajlari.md`'den.
///
/// <para><b>Neden birebir metin, neden uydurma değil:</b> uygulama bazı hataları METİNDEN tanıyor. Örneğin
/// <c>BuildRestPushFailure</c> gerekçede "fahiş fiyat" geçiyorsa genel "push reddedildi" yerine ayrı bir hata
/// kodu (<c>TradeXpress:N11:Rest:PriceOutOfBand</c>) üretiyor — çünkü o durumda ürün ESKİ, düşük fiyatla satışta
/// kalır ve kullanıcının bunu ayırt etmesi gerekir. Mock uydurma bir metin döndürseydi bu dal HİÇ sınanamazdı;
/// gerçek metinle döndürünce, üretim kodu değişmeden, gerçek HTTP'den ve gerçek ayrıştırıcıdan geçerek
/// doğru hata kodunun üretildiği kanıtlanır.</para>
/// </summary>
public static class N11MockErrorCatalog
{
    /// <summary>(32) Fahiş fiyat ARTIŞI — altın sıçradığında gerçekte alınan hata. Uygulamanın özel-durum
    /// eşlemesini tetikleyen "fahiş fiyat" ifadesini içerir.</summary>
    public const string PriceBandTooHigh =
        "Bu ürün için girdiğiniz fiyatta fahiş fiyat artışı olduğundan dolayı fiyat hatası riski içermektedir "
        + "(Maximum 25000 TL). Lütfen fiyatı kontrol ediniz. Dilerseniz mağaza destek merkezinden talep oluşturabilirsiniz.";

    /// <summary>(31) Fahiş fiyat DÜŞÜKLÜĞÜ — aynı ailenin diğer yönü.</summary>
    public const string PriceBandTooLow =
        "Bu ürün için girdiğiniz fiyatta fahiş fiyat düşüklüğü olduğundan dolayı fiyat hatası riski içermektedir "
        + "(Minimum 100 TL). Lütfen fiyatı kontrol ediniz. Dilerseniz mağaza destek merkezinden talep oluşturabilirsiniz.";

    /// <summary>Genel red — "fahiş fiyat" İÇERMEZ, dolayısıyla uygulamada genel <c>PushRejected</c> üretmelidir.
    /// İki dalın ayrıldığını kanıtlamak için gereken karşı örnek.</summary>
    public const string GenericReject =
        "Ürün bilgileri eksik veya hatalı olduğundan işlem gerçekleştirilemedi.";
}
