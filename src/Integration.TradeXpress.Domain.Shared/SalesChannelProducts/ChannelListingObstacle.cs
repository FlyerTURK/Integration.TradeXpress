namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>
/// PAZARYERİ ENGELİ — kanal-agnostik, tek değerli cevap: "bu kayıt kanalda neden satılamıyor?".
///
/// <para><b>Neden ayrı bir kavram:</b> "onaylı" ve "satışta" bayrakları durumu söyler, SEBEBİ söylemez.
/// Karalisteye alınmış bir kalem bizde "onaylı + satışta" görünebiliyordu; gönderim karşı tarafta
/// reddediliyor ve sebebi hiçbir ekranda yer almıyordu. Canlı ölçüm bunun teorik olmadığını gösterdi —
/// tek bir grubun 19 kaleminin tamamı karalistedeydi.</para>
///
/// <para><b>Bugün yalnız Trendyol dolduruyor</b> ve bu bir eksiklik DEĞİLDİR: N11 ile Etsy böyle bir beyan
/// döndürmüyor. <see cref="None"/> orada "engel yok" değil "bilgi yok" anlamına gelir; ikisini ayırmak için
/// ayrı bir durum eklemek, bugün hiçbir kararı değiştirmeyen bir ayrım olurdu.</para>
///
/// <para><b>Sıra AĞIRLIĞA göredir</b> (küçük = ağır): bir kalem hem karalistede hem kilitli olabilir.
/// Kullanıcıya iki gerekçe birden yazmak eylemi bulanıklaştırır; önce ÇÖZÜLMESİ GEREKEN söylenir.</para>
/// </summary>
public enum ChannelListingObstacle
{
    /// <summary>Bilinen bir engel yok (ya da kanal böyle bir beyan döndürmüyor).</summary>
    None = 0,

    /// <summary>Karalistede — satışa çıkamaz. Belge/itiraz süreci ister.</summary>
    Blacklisted = 1,

    /// <summary>Reddedilmiş.</summary>
    Rejected = 2,

    /// <summary>Listeleme kilitli — gönderim kabul edilmez.</summary>
    Locked = 3,

    /// <summary>Arşivlenmiş.</summary>
    Archived = 4
}
