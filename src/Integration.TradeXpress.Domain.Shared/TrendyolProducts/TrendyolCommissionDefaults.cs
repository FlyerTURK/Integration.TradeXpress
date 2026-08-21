namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol komisyon oranı — <b>GEÇİCİ YER TUTUCU</b> (2026-08-06 Hakan kararı: <i>"Sen ortalama bir rakam
/// gir ve bitir. Zaten henüz production değiliz."</i>).
///
/// <para><b>Neden sabit:</b> Trendyol komisyonu N11'deki gibi kategori ucundan GELMİYOR — kategori API'si
/// yalnız <c>id/name/parentId/isLeaf</c> döndürür. Gerçek oran satıcının kendi sözleşmesine bağlıdır ve
/// alt kategoriye, markaya, <b>satıcı seviyesine (1–5)</b>, kadın girişimci programına ve kampanya dönemine
/// göre değişir; herkese açık tek bir resmî tablo yoktur. Tek otoriter kaynak Satıcı Paneli →
/// <i>Anlaşma Bilgileri</i> ekranıdır.</para>
///
/// <para><b>Bu sabit oraya kadarki yer tutucudur (<c>PlaceholderRate</c>).</b> Öncesinde <c>resolvedCommissionRate</c> sabit <c>null</c>
/// geçiliyordu; sonuç komisyonun fiyata HİÇ girmemesiydi — sessiz ve görünmez. Yaklaşık bir oran, hiç
/// olmamasından iyidir.</para>
///
/// <para>⚠ <b>PRODUCTION'DA DEĞİŞTİRİLECEK:</b> kategori başına gerçek oran + oranın tabanı (KDV dahil mi
/// hariç mi) modellenmeli. Kaynaklar bu konuda ÇELİŞİYOR: aynı yayın hem <i>"oran KDV hariç fiyat üzerinden
/// hesaplanır, komisyona ayrıca KDV eklenir (%20 → fiilen ~%24)"</i> diyor hem tablo başlığında
/// <i>"KDV dahil"</i> yazıyor. Aradaki fark ~4 puandır ve doğrudan net kâra girer.</para>
/// </summary>
public static class TrendyolCommissionDefaults
{
    /// <summary>Yer tutucu oran (%). Kozmetik/kişisel bakım için yayınlanan %17–20 aralığının orta noktası;
    /// tüm kategoriler için geçici ortalama olarak kullanılır.</summary>
    public const decimal PlaceholderRate = 18.5m;
}
