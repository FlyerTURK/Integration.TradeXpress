namespace Integration.TradeXpress;

/// <summary>
/// Seed orchestrator'ı — tek <see cref="IDataSeedContributor"/>; yalnız <b>sırayı</b> yönetir, asıl iş
/// bucket-bazlı odaklı seeder'larda (SRP): <c>CurrencyUnitSeeder</c> / <c>ParitySeeder</c> /
/// <c>CountrySeeder</c> / <c>OrgSeeder</c>. Sıkı bağımlılık var (birimler ÖNCE — parite/marj/şirket-base
/// hepsi birime bağlı); ABP contributor'ları sırasız çalıştırdığından sıra burada tek noktada garanti edilir.
/// Idempotent (her seeder kendi var-olanı atlar).
/// </summary>
public class TradeXpressDataSeedContributor(
    CurrencyUnitSeeder currencyUnitSeeder,
    ParitySeeder paritySeeder,
    CountrySeeder countrySeeder,
    Integration.TradeXpress.Geography.GeographySeeder geographySeeder,
    CashSeeder cashSeeder,
    Integration.TradeXpress.Services.ServiceSeeder serviceSeeder,
    Integration.TradeXpress.Futures.FutureSeeder futureSeeder,
    Integration.TradeXpress.Scraps.ScrapSeeder scrapSeeder,
    Integration.TradeXpress.Metals.MetalSeeder metalSeeder,
    OrgSeeder orgSeeder,
    Integration.TradeXpress.Vouchers.Balance.BalanceLedgerBackfiller balanceLedgerBackfiller,
    Integration.TradeXpress.MultiCompany.CompanyOwnedBackfiller companyOwnedBackfiller,
    CountryReferenceBackfiller countryReferenceBackfiller,
    Integration.TradeXpress.Authorization.ScopedGrantSeeder scopedGrantSeeder)
    : IDataSeedContributor, ITransientDependency
{
    #region Fields

    private readonly CurrencyUnitSeeder _currencyUnitSeeder = currencyUnitSeeder;
    private readonly ParitySeeder _paritySeeder = paritySeeder;
    private readonly CountrySeeder _countrySeeder = countrySeeder;
    private readonly Integration.TradeXpress.Geography.GeographySeeder _geographySeeder = geographySeeder;
    private readonly CashSeeder _cashSeeder = cashSeeder;
    private readonly Integration.TradeXpress.Services.ServiceSeeder _serviceSeeder = serviceSeeder;
    private readonly Integration.TradeXpress.Futures.FutureSeeder _futureSeeder = futureSeeder;
    private readonly Integration.TradeXpress.Scraps.ScrapSeeder _scrapSeeder = scrapSeeder;
    private readonly Integration.TradeXpress.Metals.MetalSeeder _metalSeeder = metalSeeder;
    private readonly OrgSeeder _orgSeeder = orgSeeder;
    private readonly Integration.TradeXpress.Vouchers.Balance.BalanceLedgerBackfiller _balanceLedgerBackfiller = balanceLedgerBackfiller;
    private readonly Integration.TradeXpress.MultiCompany.CompanyOwnedBackfiller _companyOwnedBackfiller = companyOwnedBackfiller;
    private readonly CountryReferenceBackfiller _countryReferenceBackfiller = countryReferenceBackfiller;
    private readonly Integration.TradeXpress.Authorization.ScopedGrantSeeder _scopedGrantSeeder = scopedGrantSeeder;

    #endregion

    #region Seeding

    /// <summary>
    /// Bu property <c>true</c> ise <see cref="OrgSeeder"/> ATLANIR. Onboarding (TenantAppService impersonation
    /// akışı) şirket grafını kendisi tanımladığından varsayılan "MRK" şirketini istemez — yoksa çift kayıt.
    /// </summary>
    public const string SkipOrgSeedProperty = "TradeXpress:SkipOrgSeed";

    public async Task SeedAsync(DataSeedContext context)
    {
        // (1) Merkezi referans yalnız host'ta (TenantId=null); tenant'lar paylaşır (null‖own).
        if (context.TenantId == null)
        {
            await _currencyUnitSeeder.SeedCatalogAsync(); // birimler + TRY ham kuru — HER ŞEYDEN ÖNCE
            await _paritySeeder.SeedAsync();              // host-global pariteler
            await _countrySeeder.SeedAsync();             // host-global ülke kataloğu (desteklenen birimli ülkeler)
            await _geographySeeder.SeedAsync();           // ISO 3166-1 tam ülke listesi (249) + TR il/ilçe (N11'den) + US eyalet
            await _cashSeeder.SeedAsync();                // host-global nakit kataloğu (Type=Cash birimlerden türetilir)
        }

        // (2) Marjlar her tenant'ta (host dahil) — host'un merkezi düzeltme marjı da burada.
        await _currencyUnitSeeder.SeedMarginsAsync(context.TenantId);

        // (3) Org ağacı yalnız tenant'a aittir (host'ta company yok). Onboarding org'u kendi kuruyorsa atla.
        if (context.TenantId != null && context[SkipOrgSeedProperty] is not true)
        {
            await _orgSeeder.SeedHqCompanyAsync(context.TenantId);
        }

        // (4) EMTİA KATALOĞU — hepsi PER-COMPANY (ICompanyOwned, görev #4) → ŞİRKETLER KURULDUKTAN SONRA.
        //     Her seeder tenant'ın tüm şirketlerini dolaşır; host'ta şirket olmadığı için host'ta çalışmaz.
        //     Service eskiden host-global seed ediliyordu (yanlış katman): artık şirkete ait bir emtia.
        if (context.TenantId != null)
        {
            // (4a) SEEDER'LARDAN ÖNCE yetim sahiplendirme — SIRA HAYATİ.
            //      Host dalındaki çağrı (adım 6) TEK BAŞINA YETMEZ: merkez şirket adım (3)'te, yani TENANT
            //      dalında kurulur. Şirketi olmayan bir tenant'ta host geçişi hiç HQ göremez, atlar; hemen
            //      ardından aşağıdaki seeder'lar "bu şirkette kayıt yok" deyip TAZE set açar ve yetimler
            //      KALICI olarak gölgelenir (sonraki koşuda kod artık dolu → hep "kod meşgul" diye atlanır).
            //      Bu, sınıfın önlemek için var olduğu 2026-07-25 olayının ta kendisiydi — kod incelemesi
            //      host-dalı-tek-başına varsayımımı çürüttü. İdempotent: yetim yoksa ucuz no-op.
            await _companyOwnedBackfiller.BackfillAllTenantsAsync();

            await _futureSeeder.SeedAsync();
            await _scrapSeeder.SeedAsync();
            await _serviceSeeder.SeedAsync();
            await _metalSeeder.SeedAsync();
        }

        // (5) Bakiye ledger'ı (Path B) — mevcut voucher'lardan birim-net etkileri doldur (idempotent;
        //     doluysa atlar). Voucher'lar tenant-scoped → yalnız tenant'ta.
        if (context.TenantId != null)
        {
            await _balanceLedgerBackfiller.BackfillCurrentTenantAsync();
        }

        // (6) Çok-şirket güvenlik sınırı geçiş backfill'i: ICompanyOwned'a taşınan kayıtlarda migration'ın
        //     Guid.Empty bıraktığı CompanyId'yi doldur (idempotent; boş satır yoksa no-op). Kapsam:
        //     SubAccount/Vault parent'tan + 7 EMTİA ailesi tenant'ın merkez şirketinden (görev #4).
        //     Backfill TENANT-AGNOSTİK (Disable<IMultiTenant> ile TÜM tenant'ları kapsar) → host koşusunda
        //     BİR KEZ çağrılır; her tenant için ayrı çağrıya gerek yok (aksi halde yalnız seed edilen
        //     tenant'ların kayıtları dolar — önceki bug buydu). Yeni kayıtlar zaten CompanyId ile oluşur (ctor).
        //
        //     SIRA KRİTİK — bu adım HOST dalındadır ve host geçişi TÜM tenant geçişlerinden ÖNCE biter
        //     (TradeXpressDbMigrationService: host SeedDataAsync await edilir, tenant döngüsü sonra başlar).
        //     Böylece adım (4)'teki emtia seeder'ları çalıştığında yetim satır KALMAMIŞ olur. Tenant dalına
        //     İKİNCİ bir çağrı EKLEME: orada adım (6) adım (4)'ten SONRA gelir — tam istenmeyen sıra.
        if (context.TenantId == null)
        {
            await _companyOwnedBackfiller.BackfillAllTenantsAsync();

            // Country id-only geçiş backfill'i: string kodlardan (Company.CountryCode /
            // Country.DefaultCurrencyCode) yeni Guid kolonları doldur. Country + CurrencyUnit
            // seed'lerinden SONRA (adım 1) koşmalı ki kod→id eşleşecek kayıtlar mevcut olsun;
            // tenant-agnostik (Disable<IMultiTenant>) → host koşusunda BİR KEZ yeter.
            await _countryReferenceBackfiller.BackfillAllTenantsAsync();
        }

        // (7) Kapsam grant geri-uyumu (Faz 4 working-context): mevcut kullanıcılara tenant-geneli Grant
        //     garanti et (rollüye rol-başı, rolsüze coğrafi-only) → resolution-time doğrulama devreye
        //     girince kimse şube seçiminde kilitlenmez (idempotent; zaten grant'ı olan atlanır). Kullanıcılar
        //     tenant-scoped → yalnız tenant'ta.
        if (context.TenantId != null)
        {
            await _scopedGrantSeeder.SeedCurrentTenantAsync();
        }
    }

    #endregion
}
