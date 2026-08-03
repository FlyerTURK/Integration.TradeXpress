namespace Integration.TradeXpress.Inbox;

/// <summary>Ortak gelen kutusu panosunun sayısal sınırları — pano ile sağlayıcıların ORTAK sözleşmesi
/// (tek kaynak: sağlayıcılar kendi sınırlarını uydurmaz, pano onlara bu değeri verir).</summary>
public static class InboxConsts
{
    /// <summary>Bir kartta gösterilecek "son öğe" adedi. Pano ÖZETTİR: kart birkaç satırlık bir vitrindir,
    /// tam liste türün KENDİ ekranındadır. Sayı küçük tutulur ki kart açılışı tek hafif sorguyla dönsün ve
    /// pano N sağlayıcıda bile ağırlaşmasın.</summary>
    public const int RecentItemCount = 5;
}
