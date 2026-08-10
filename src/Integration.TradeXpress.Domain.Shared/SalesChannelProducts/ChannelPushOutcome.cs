namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// Bir gönderim denemesinin SONUCU — delil defterinin (push history) her satırında zorunlu.
///
/// <para><b>Neden eklendi</b> (2026-08-10 Hakan kararı): defter başlangıçta YALNIZ başarılı gönderimi
/// yazıyordu; reddedilen gönderim hiç iz bırakmıyordu. Sonuç: "14:03'te 175 TL göndermeyi denedik, kanal
/// 'barcode not found' dedi" cümlesi kurulamıyordu — başarısızlık, sistemin hiç denemediği durumdan
/// ayırt edilemiyordu. Otonom fiyat/stok güncellemesi devreye girdiğinde bu ayrım şart: bir fiyatın
/// kanala YANSIMAMIŞ olmasının sebebini ancak deneme kaydı söyleyebilir.</para>
///
/// <para><b>Eski tasarımın koruduğu şey KORUNUR:</b> "başarısızı yazmamak" kuralının asıl amacı, reddedilen
/// bir gönderimi <b>başarılı sanmayı</b> önlemekti. O koruma bu enum'la daha güçlü hâle gelir — satır artık
/// susmak yerine <see cref="Failed"/> diyor. <c>LastSent*</c> terfi mantığı DEĞİŞMEZ: kıyas tabanını yalnız
/// <see cref="Succeeded"/> ilerletir, aksi hâlde bir sonraki tur "değişiklik yok" deyip hiç ulaşmamış fiyatı
/// sessizce atlardı.</para>
///
/// <para><b>Zorunlu ctor parametresidir</b> (varsayılana bırakılmaz): unutulduğunda başarısız bir gönderimin
/// başarılı görünmesi, tam da bu defterin önlemek için var olduğu hatadır.</para>
/// </summary>
public enum ChannelPushOutcome : byte
{
    /// <summary>Kanal kabul etti — gönderilen değerler karşı tarafa ULAŞTI.</summary>
    Succeeded = 0,

    /// <summary>Kanal reddetti ya da gönderim hata aldı — değerler ULAŞMADI. Gerekçe satırdaki hata
    /// metnindedir; kısmi başarı da (batch'in bir kısmı düştü) buraya girer, çünkü hangi SKU'nun düştüğü
    /// güvenilir biçimde eşlenemiyor ve yarım terfi tabanı kirletirdi.</summary>
    Failed = 1,
}
