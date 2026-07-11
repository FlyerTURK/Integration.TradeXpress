namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Yan-maliyet kaleminin <b>fişleme hedefi</b> (kullanıcı kararı 2026-07-10) — satış gerçekleştiğinde kalemin
/// VoucherLine'a hangi karşı tarafla dönüşeceğini belirler. BU DİLİMDE FİŞ YAZILMAZ — yalnız veri bağı kurulur;
/// ileride sipariş→fiş akışı bu ayarı okuyacak ("N11'e bu ay ne kadar komisyon borçlandık" cari izlemesi).
/// Kullanıcı eşlemesi: KARGO → "Yurtiçi Kargo" carisi; SİGORTALI GÖNDERİM → "Loomis" carisi; KOMİSYON → satış
/// kanalının carisi; PAKETLEME → şimdilik Expense (genel gider, karşı cari yok).
/// </summary>
public enum SideCostPostingMode : byte
{
    /// <summary>Karşı taraf cari hesabı — kalem, seçilen cari hesaba (Account/SubAccount) borç/alacak yazar.</summary>
    CounterpartyAccount = 1,

    /// <summary>Genel gider — karşı cari YOK; kalem gider olarak işlenir.</summary>
    Expense = 2,
}
