namespace Integration.TradeXpress.ChannelQuestions;

/// <summary>
/// Kanal sorusu SENKRON sabitleri — hepsi 2026-08-01 CANLI keşfinin ölçtüğü sınırlardan türer
/// (<c>.claude/research/n11-questions/canli-kesif-2026-08-01.md</c>).
///
/// <para><b>Belirleyici sınır: DAKİKADA 1 ÇAĞRI.</b> Paralellik aşmıyor (3 eşzamanlıdan 1'i geçti), kota TÜM
/// hesap için ortak. Bu yüzden çekim tek merkezden (worker) ve TURDA TEK İŞ ADIMI olarak yürür; buradaki
/// sayılar "kaç çağrı" değil "her çağrıda ne kadar iş" sorusunu ayarlar.</para>
/// </summary>
public static class ChannelQuestionSyncConsts
{
    /// <summary>Sayfa boyutu. Canlıda <c>pageSize=100</c> KABUL EDİLDİ; dakikada tek çağrı hakkımız olduğu için
    /// sayfa başına azami veriyi almak seed süresini doğrudan kısaltır (100 yerine 20 seçmek aynı geçmişi 5 kat
    /// daha uzun sürede çekerdi).</summary>
    public const int PageSize = 100;

    /// <summary>Geçmiş seedi ÜST ÜSTE bu kadar boş ay görünce durur. Kanalın açılış tarihi API'den bilinmiyor —
    /// "nereye kadar gerileyeceğiz" sorusunun tek ölçülebilir cevabı veri yokluğudur. 12 ay seçildi çünkü satıcı
    /// bir sezon boyunca (ör. yalnız yılbaşı kampanyasında) soru almamış olabilir; 2-3 boş ay gerçek geçmişi
    /// erken keser.</summary>
    public const int EmptyMonthsBeforeStop = 12;

    /// <summary>Seed GÜVENLİK TAVANI (ay). Boş-ay kuralı hiç tetiklenmese bile seed sonsuza kadar geriye
    /// gitmesin: 60 ay ≈ 60 dakika kota tüketimi demektir ve daha eski soru operasyonel olarak ölüdür.</summary>
    public const int MaxSeedMonths = 60;

    /// <summary>Kanal başına RUTİN tazeleme eşiği (dakika) — Hakan'ın "5 dakikada bir tazeleme" kararı.
    /// <b>Worker periyodu DEĞİLDİR</b> (o 1 dakikadır): worker her dakika bir iş adımı yürütür, ama bir kanalın
    /// rutin tazelemesi ancak son tazelemeden bu kadar süre geçmişse ADAY olur. Böylece boştaki dakikalar seed'e
    /// gider, kota aşılmaz ve tazeleme yine 5 dakikada bir gerçekleşir.</summary>
    public const int RoutineRefreshMinutes = 5;

    /// <summary>Soruya iliştirilmiş müşteri fotoğraflarının BAĞLANTILARININ saklandığı ABP extra-property adı.
    /// <para><b>Neden ExtraProperties (2026-08-01 kararı):</b> Hakan "şimdilik yalnız bağlantı sakla, DAM'a indirme"
    /// dedi. Bağlantı listesi ne filtrelenir ne sıralanır ne de raporlanır — yalnız cevap ekranında gösterilir.
    /// Bunun için <c>ChannelQuestion</c>'a kolon eklemek şema borcu yaratırdı; ExtraProperties kolonu tabloda
    /// ZATEN var (AddChannelQuestions migration'ı) → sıfır şema değişikliği.</para>
    /// <para><b>Biçim: satır-sonu ile ayrılmış düz metin.</b> ABP <c>GetProperty&lt;T&gt;</c> ilkel-olmayan tipleri
    /// DESTEKLEMEZ (AbpException) — dizi olarak yazılan değer okurken patlardı. URL'ler satır sonu içeremediği için
    /// ayraç güvenlidir.</para></summary>
    public const string ImageUrlsPropertyName = "QuestionImageUrls";

    /// <summary><see cref="ImageUrlsPropertyName"/> değerinin ayracı (bkz. gerekçe orada).</summary>
    public const string ImageUrlsSeparator = "\n";
}
