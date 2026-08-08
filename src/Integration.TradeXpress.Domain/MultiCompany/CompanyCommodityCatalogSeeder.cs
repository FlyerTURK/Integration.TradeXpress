using System.Threading.Tasks;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// YENİ ŞİRKETİN EMTİA KATALOĞUNU KURAR — Maden · Hurda · Vadeli · Hizmet.
///
/// <para><b>Kapatılan açık:</b> emtia katalogları PER-COMPANY'dir (CLAUDE.md §6) ve seeder'lar bunu doğru
/// yapıyordu — ama YALNIZ <c>TradeXpressDataSeedContributor</c>'dan tetikleniyorlardı, yani DbMigrator
/// koşumundan ya da tenant onboarding'inin ikinci seed pass'inden. <c>CompanyAppService.CreateAsync</c> hiçbir
/// seeder çağırmıyordu → Şirketler ekranından (ve tenant grafı güncellemesinden) açılan şirket dört katalogda
/// da BOŞ doğuyordu. Hata sessiz: şirket açılır, listeler boş gelir, kullanıcı "henüz girmedim" sanır.</para>
///
/// <para><b>Neden seeder'lar yeniden yazılmadı:</b> dördü de zaten şirket-farkındalı ve idempotent — mevcut
/// kodu olan şirketi atlar, soft-delete edilmiş kaydı DİRİLTMEZ. Her birine "tek şirket" aşırı yüklemesi
/// eklemek aynı deseni dört dosyaya yaymak ve var olan atlama mantığını çoğaltmak olurdu.</para>
///
/// <para><b>Bilinen bedel:</b> çağrı mevcut TÜM şirketleri dolaşır (her biri için birkaç SELECT). Şirket açmak
/// nadir bir işlem olduğundan bu kabul edilebilir ve bir yan faydası var: kataloğu eksik kalmış eski bir şirket
/// varsa bu koşumda KENDİLİĞİNDEN tamamlanır. Şirket sayısı yüzleri bulursa seeder'lara tek-şirket yolu
/// eklenmelidir.</para>
///
/// <para><b>Kapsam:</b> yalnız SİSTEM kataloğu olan aileler. Mamül/Taş/Mücevher kullanıcının kendi tanımladığı
/// kataloglardır — seeder'ları YOKTUR ve olmamalıdır (uydurma kayıt üretmek kullanıcının verisini kirletir).</para>
/// </summary>
public class CompanyCommodityCatalogSeeder(
    MetalSeeder metalSeeder,
    ScrapSeeder scrapSeeder,
    FutureSeeder futureSeeder,
    ServiceSeeder serviceSeeder) : ITransientDependency
{
    private readonly MetalSeeder _metalSeeder = metalSeeder;
    private readonly ScrapSeeder _scrapSeeder = scrapSeeder;
    private readonly FutureSeeder _futureSeeder = futureSeeder;
    private readonly ServiceSeeder _serviceSeeder = serviceSeeder;

    /// <summary>Eksik katalog kayıtlarını tamamlar. Çağıranın ambient UoW'unda çalışır.</summary>
    public async Task SeedAsync()
    {
        await _metalSeeder.SeedAsync();
        await _scrapSeeder.SeedAsync();
        await _futureSeeder.SeedAsync();
        await _serviceSeeder.SeedAsync();
    }
}
