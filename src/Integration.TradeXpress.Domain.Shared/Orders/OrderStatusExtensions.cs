namespace Integration.TradeXpress.Orders;

/// <summary><see cref="OrderStatus"/> yorum yardımcıları — tanım TEK yerde durur ki farklı akışlar
/// (rezervasyon kapısı, iade süreci, raporlar) "hangi durum bitmiş sayılır" sorusunu ayrı ayrı
/// cevaplamasın.</summary>
public static class OrderStatusExtensions
{
    /// <summary>Sipariş TERMİNAL mi — akışı bitmiş, yeni stok taahhüdü doğurmayacak durumlar.
    ///
    /// <para><b>Neden gerekli:</b> rezervasyon kurulumu sipariş durumunu hiç okumuyordu; canlıdaki 106 teslim
    /// edilmiş 2017-2018 siparişi, ürün eşleşmeleri sihirbazla kuruldukça kendiliğinden rezervasyon fişi
    /// üretecek "kurulu bir tuzak"tı — yıllar önce teslim edilmiş mal bugün stoktan ayrılırdı.</para>
    ///
    /// <para><b><see cref="OrderStatus.Unknown"/> terminal DEĞİLDİR:</b> çözülemeyen ham durum "bitmiş" sayılamaz —
    /// öyle sayılsaydı, eşleme tablosunda eksik olan tek bir kanal kodu yüzünden canlı siparişler sessizce
    /// rezervasyonsuz kalırdı. Belirsizlik, rezervasyonu ATLAMA gerekçesi olamaz.</para></summary>
    public static bool IsTerminal(this OrderStatus status)
    {
        return status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;
    }
}
