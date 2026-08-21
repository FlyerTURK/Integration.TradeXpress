namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// Bir tenant'ın TEK kanal-tipi (N11 ya da Trendyol) mutabakat turunun özeti — işçi Information loglar,
/// test doğrular. Sayaçların anlamı:
/// <list type="bullet">
///   <item><see cref="Scanned"/> — incelenen kanal-ürün kaydı (aktif + pasif; bekleyen push'lular hariç).</item>
///   <item><see cref="SkippedPending"/> — çözülmemiş push/batch'i olan kayıt: kuyruk çözücüleriyle YARIŞILMAZ
///   (gözlem, henüz kanala ulaşmamış gönderimin öncesini gösterebilir; taban yazılsaydı terfi anında geri ezilirdi).</item>
///   <item><see cref="DriftedRecords"/> / <see cref="CorrectedSkus"/> — tabanı kanal gözlemine çekilen kayıt/SKU.</item>
///   <item><see cref="PassiveDrifts"/> — PASİF kayıtta kanal hâlâ satılabilir adet gösteriyor; yalnız LOG
///   (taban 0'a çekilmez ki otomatik push tetiklenmesin — karar kullanıcıya kalır).</item>
///   <item><see cref="MissingSkus"/> — tabanı dolu ama kanal listelemiyor; yalnız LOG (listelemenin elle
///   silinmesi değer sapmasından farklı bir durumdur; otomatik yeniden-oluşturma tetiklenmez).</item>
///   <item><see cref="FailedRecords"/> / <see cref="FailedChannels"/> — kayıt-başı arıza / listelemesi
///   okunamayan kanal (o kanalın kayıtları o turda taranmaz).</item>
///   <item><see cref="SkippedNoAdmin"/> — tenant admin'i yok, tur atlandı (sessiz geçilmedi, loglandı).</item>
/// </list>
/// </summary>
public sealed record ChannelReconciliationReport(
    int Scanned,
    int SkippedPending,
    int DriftedRecords,
    int CorrectedSkus,
    int PassiveDrifts,
    int MissingSkus,
    int FailedRecords,
    int FailedChannels,
    bool SkippedNoAdmin)
{
    /// <summary>Boş tur (bekleyen kayıt yok) — sayaç okunurluğu için tek yerde.</summary>
    public static ChannelReconciliationReport Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, SkippedNoAdmin: false);

    /// <summary>Admin bulunamadı — hiçbir kayıt taranmadı (kaç kaydın beklediğini çağıran loglar).</summary>
    public static ChannelReconciliationReport NoAdmin { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, SkippedNoAdmin: true);

    /// <summary>Turda kayda değer bir şey oldu mu (log gürültüsünü süzmek için).</summary>
    public bool HasActivity
    {
        get
        {
            return Scanned > 0 || SkippedPending > 0 || FailedRecords > 0 || FailedChannels > 0 || SkippedNoAdmin;
        }
    }
}
