using System;

namespace Integration.TradeXpress.Mocks.N11;

/// <summary>
/// N11 sahte sunucusunun ayarları (config bölümü <c>"N11:Mock"</c>).
///
/// <para><b>Üç kapı birden gerekir</b> — biri bile eksikse sahte sunucu devreye girmez:</para>
/// <list type="number">
///   <item><c>IsDevelopment()</c> — üretim ortamında uçlar HİÇ haritalanmaz.</item>
///   <item><see cref="Enabled"/> — bu ayar.</item>
///   <item><c>N11:Endpoints:BaseUrl</c> mock'u göstermeli — göstermiyorsa hiçbir istek buraya gelmez,
///     uçlar boşta durur.</item>
/// </list>
///
/// <para>Üçünün ayrı olması bilinçli: yanlışlıkla açık kalan tek bir bayrak yeterli olmamalı.</para>
///
/// <para><b>⚠ TLS TUZAĞI (2026-08-05'te yaşandı):</b> <c>BaseUrl</c> host'un SERTİFİKASINDAKİ adla birebir aynı
/// olmalıdır. Uygulama kendi kendine bağlanırken de sertifika doğrulaması yapılır — sertifika
/// <c>umut.taile7a850.ts.net</c> adına olduğu için <c>https://localhost:44318</c> yazmak
/// <i>"The SSL connection could not be established"</i> verir ve HER N11 çağrısı düşer. Curl testleri bunu
/// yakalamamıştı (<c>-k</c> ile doğrulama atlanıyordu), round-trip testi de düz HTTP kullandığı için
/// bu yolu hiç sınamıyor.</para>
/// </summary>
public sealed class N11MockOptions
{
    public const string SectionName = "N11:Mock";

    /// <summary>Sahte sunucu uçları haritalansın mı. Varsayılan KAPALI — açmak bilinçli bir eylem olmalı.</summary>
    public bool Enabled { get; set; }

    /// <summary>Durum deposunun JSON dosyası. Boşsa host'un içerik kökünde <c>App_Data/n11-mock-store.json</c>.
    ///
    /// <para><b>Neden dosya, neden bellek değil:</b> host geliştirme sırasında sürekli yeniden başlıyor;
    /// salt-bellek depo her başlatmada push'u sıfırdan yapmayı gerektirirdi. <b>Neden DB değil:</b> yalnız
    /// geliştirmede kullanılan bir özellik için 14 tenant'ın paylaştığı tek fiziksel veritabanına kalıcı şema
    /// eklemek orantısız. JSON ayrıca ELLE düzenlenebilir — senaryoyu dosyadan kurarsın.</para></summary>
    public string? StorePath { get; set; }

    /// <summary>Task kaç kez sorgulandıktan SONRA <c>PROCESSED</c>'e geçsin (0 = ilk sorguda hazır).
    /// Kuyruk davranışını taklit etmek için; gerçek N11'de task anında olgunlaşmaz.</summary>
    public int QueuedPollsBeforeProcessed { get; set; }

    /// <summary>Yapay gecikme (ms) — ağ gecikmesini taklit eder. 0 = gecikme yok.</summary>
    public int LatencyMs { get; set; }
}
