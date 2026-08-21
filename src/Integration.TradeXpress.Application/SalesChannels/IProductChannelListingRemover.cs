using System;
using System.Threading.Tasks;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// Bir ŞABLON ÜRÜN silinirken o ürüne bağlı KANAL kayıtlarını (ve bağımlı grafını) temizleyen kanal-başı temizleyici.
///
/// <para><b>Neden var (2026-08-06 canlı vakası):</b> kanal kaydı şablon ürüne yalnız <c>Guid</c> ile bağlıdır
/// (aggregate'ler arası id-only konvansiyonu — <c>NavigationConventionTests</c>) → referans bütünlüğünü DB ZORLAMAZ.
/// <c>ProductAppService.DeleteAsync</c> varyantı, varyant uzantısını, reçete satırlarını ve medya bağlarını
/// temizliyordu ama KANAL kayıtlarına dokunmuyordu; sonuç ölü <c>ProductId</c> taşıyan kanal kayıtlarıydı. Bu kayıtlar
/// açılamaz, düzenlenemez, push edilemez ve içe aktarımı topyekûn kilitler — kullanıcı "yereli silip mağazadan
/// sıfırdan çekeyim" dediğinde 18 öksüz kayıt oluşup içe aktarım "Ürün bulunamadı" ile ölüyordu.</para>
///
/// <para><b>Neden arayüz, neden AppService çağrısı değil:</b> kanal AppService'lerinin <c>DeleteAsync</c>'i
/// <c>SalesChannels.Delete</c> izni ister; ürün silen kullanıcının o izne sahip olması ZORUNLU DEĞİLDİR. Ayrıca
/// <c>ProductAppService</c>'in üç kanalı tek tek tanıması, dördüncü kanal eklendiğinde onu da düzenlemeyi gerektirirdi.
/// Temizleyiciler <c>IEnumerable&lt;IProductChannelListingRemover&gt;</c> olarak enjekte edilir → yeni kanal yalnız
/// KENDİ temizleyicisini ekler, ürün tarafı DEĞİŞMEZ.</para>
/// </summary>
public interface IProductChannelListingRemover
{
    /// <summary>Verilen şablon ürüne bağlı TÜM kanal kayıtlarını bağımlılarıyla (override başlıkları · reçete
    /// satırları · özellik/değer grafı) soft-delete eder. Kayıt yoksa no-op.</summary>
    Task RemoveForProductAsync(Guid productId);

    /// <summary>ANA ÜRÜN PASİFLEŞİNCE bağlı kanal kayıtlarını pasifler (2026-08-17 Hakan kararı "A" — tek yönlü baskı:
    /// ürün satıştan çekilince kanal listing'i de çekilir; ürün yeniden aktif olunca kanal ürünleri OTOMATİK
    /// açılMAZ, kanal kanal insan kararı). Pazaryerindeki satış da durdurulur — kanalın yeteneği neyse o:
    /// Trendyol'da uzak ARŞİV, N11'de bilinen tüm SKU'lara ADET-0 (2026-08-21 kararı; arşiv ucu yok). İki yolda da
    /// red ürünün pasifleşmesini geri alır (aynı transaction). Etsy bugün yalnız yerel bayrak.</summary>
    Task DeactivateForProductAsync(Guid productId);
}
