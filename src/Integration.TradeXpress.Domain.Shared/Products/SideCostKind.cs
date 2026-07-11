namespace Integration.TradeXpress.Products;

/// <summary>
/// Reçete satırının <b>yan-maliyet türü</b> — kanal gider ayarlarından (<c>SideCostSettings</c>) OTOMATİK üretilen
/// satırları kullanıcı satırlarından ayırır. Null = kullanıcı satırı (elle girilmiş). Dolu = otomatik yönetilen
/// satır: idempotent reconcile'ın (SideCostRecipeComposer) ANAHTARI — aynı türden satır zaten varsa composer
/// DOKUNMAZ (kullanıcı düzeltmiş olabilir), yoksa ekler; "yeniden uygula" işaretli satırları tazeler.
///
/// <para><b>Fiş hizalaması (2026-07-10):</b> bu satırlar yalnız fiyat girdisi değil — satış gerçekleştiğinde GERÇEK
/// finansal olaylardır (komisyon kesintisi, kargo/Loomis ödemesi). İleride sipariş→fiş akışında her tür, kanal
/// ayarındaki hizmet kartı (ServiceId) + karşı cari (PostingMode/Account) ile VoucherLine'a dönüşecek.</para>
/// </summary>
public enum SideCostKind : byte
{
    /// <summary>Paketleme — kanal başına sabit tutar. Fişleme varsayılanı: genel gider (karşı cari YOK).</summary>
    Packaging = 1,

    /// <summary>Kargo — kanal başına sabit tutar. Fişleme varsayılanı: karşı cari (ör. "Yurtiçi Kargo").</summary>
    Cargo = 2,

    /// <summary>Sigortalı gönderim (Loomis deseni) — kargoya bağlı OPSİYONEL kalem; sabit tutar YA DA
    /// gönderi-değeri-yüzdesi. Varsayılan KAPALI; varyant bazında açılır.</summary>
    InsuredShipping = 3,

    /// <summary>Kanal komisyonu — GrossUp (taban ÷ (1−oran/100)) satırı, reçetenin EN SONUNDA (sabit giderler de
    /// komisyona tabi). Oran: N11 kategoriden, Trendyol/Etsy kanal varsayılanından.</summary>
    Commission = 4,

    /// <summary>Kanal sabit satış bedeli — satış başına sabit ücret (Etsy: listing $0,20 + payment $0,25 = $0,45).</summary>
    ChannelFixed = 5,
}
