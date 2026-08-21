using System;

namespace Integration.TradeXpress.Orders;

/// <summary>
/// Sipariş senkron worker'ının çalışma anahtarları (<c>appsettings.json</c> → <c>Orders:Sync</c>).
/// </summary>
public class OrderSyncOptions
{
    /// <summary>Yapılandırma bölümü.</summary>
    public const string SectionName = "Orders:Sync";

    /// <summary>DOLU kanalların periyodik DELTA çekimi açık mı — <b>VARSAYILAN KAPALI</b>.
    ///
    /// <para><b>Neden kapalı doğuyor:</b> açık olduğunda worker, canlıdaki gerçek pazaryeri kimliğiyle 2
    /// dakikada bir GERÇEK API'ye çıkmaya başlar ve çektiği her sipariş rezervasyon zincirini tetikler (stok
    /// düşer). Bu, kodun merge edilmesiyle değil <b>bilinçli bir kararla</b> başlamalıdır. Deploy ya da restart
    /// bu bayrağı DELEMEZ: açmak tek satırlık açık bir config değişikliğidir.</para>
    ///
    /// <para>Açmadan önceki koşullar (go-live ön koşulları): rezervasyon kurulumunun atomikliği · terminal statü
    /// guard'ı · N11 fiyat parse onarımı · kalem↔ürün eşleştirme yolu · iptal kararlarının kullanıcı yüzü.
    /// Bunlar olmadan delta, düzeltilmemiş verinin üzerine sürekli yazan bir motor olurdu.</para></summary>
    public bool DeltaEnabled { get; set; }

    /// <summary>Seed denemesi BOŞ dönen kanal bu süre boyunca yeniden denenmez.
    /// <para>Boş bir kanalın 2 dakikada bir 40 günlük tam taramaya çıkması throttle bütçesini yakıyordu ve
    /// hiçbir şey bulmuyordu. Cooldown yalnız SEED kolunu ilgilendirir — delta kolu dolu kanalda ucuz dar
    /// pencereyle çalışır.</para></summary>
    public TimeSpan EmptyChannelCooldown { get; set; } = TimeSpan.FromMinutes(30);
}
