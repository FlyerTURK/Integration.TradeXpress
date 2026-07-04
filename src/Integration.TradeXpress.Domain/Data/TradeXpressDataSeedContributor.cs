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
    CashSeeder cashSeeder,
    Integration.TradeXpress.Services.ServiceSeeder serviceSeeder,
    Integration.TradeXpress.Futures.FutureSeeder futureSeeder,
    Integration.TradeXpress.Scraps.ScrapSeeder scrapSeeder,
    Integration.TradeXpress.Metals.MetalSeeder metalSeeder,
    OrgSeeder orgSeeder,
    Integration.TradeXpress.Vouchers.Balance.BalanceLedgerBackfiller balanceLedgerBackfiller,
    Integration.TradeXpress.MultiCompany.CompanyOwnedBackfiller companyOwnedBackfiller,
    Integration.TradeXpress.Authorization.ScopedGrantSeeder scopedGrantSeeder)
    : IDataSeedContributor, ITransientDependency
{
    #region Fields

    private readonly CurrencyUnitSeeder _currencyUnitSeeder = currencyUnitSeeder;
    private readonly ParitySeeder _paritySeeder = paritySeeder;
    private readonly CountrySeeder _countrySeeder = countrySeeder;
    private readonly CashSeeder _cashSeeder = cashSeeder;
    private readonly Integration.TradeXpress.Services.ServiceSeeder _serviceSeeder = serviceSeeder;
    private readonly Integration.TradeXpress.Futures.FutureSeeder _futureSeeder = futureSeeder;
    private readonly Integration.TradeXpress.Scraps.ScrapSeeder _scrapSeeder = scrapSeeder;
    private readonly Integration.TradeXpress.Metals.MetalSeeder _metalSeeder = metalSeeder;
    private readonly OrgSeeder _orgSeeder = orgSeeder;
    private readonly Integration.TradeXpress.Vouchers.Balance.BalanceLedgerBackfiller _balanceLedgerBackfiller = balanceLedgerBackfiller;
    private readonly Integration.TradeXpress.MultiCompany.CompanyOwnedBackfiller _companyOwnedBackfiller = companyOwnedBackfiller;
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
            await _countrySeeder.SeedAsync();             // host-global ülke kataloğu
            await _cashSeeder.SeedAsync();                // host-global nakit kataloğu (Type=Cash birimlerden türetilir)
            await _serviceSeeder.SeedAsync();             // host-global hizmet kataloğu (şu an boş — gerçek liste bekleniyor)
        }

        // (2) Marjlar her tenant'ta (host dahil) — host'un merkezi düzeltme marjı da burada.
        await _currencyUnitSeeder.SeedMarginsAsync(context.TenantId);

        // (3) Vadeli + Hurda yalnız tenant'a (ERPPROV3 paritesi; host'ta yok). Birimlerden sonra.
        if (context.TenantId != null)
        {
            await _futureSeeder.SeedAsync();
            await _scrapSeeder.SeedAsync();
            await _metalSeeder.SeedAsync();
        }

        // (4) Org ağacı yalnız tenant'a aittir (host'ta company yok). Onboarding org'u kendi kuruyorsa atla.
        if (context.TenantId != null && context[SkipOrgSeedProperty] is not true)
        {
            await _orgSeeder.SeedHqCompanyAsync(context.TenantId);
        }

        // (5) Bakiye ledger'ı (Path B) — mevcut voucher'lardan birim-net etkileri doldur (idempotent;
        //     doluysa atlar). Voucher'lar tenant-scoped → yalnız tenant'ta.
        if (context.TenantId != null)
        {
            await _balanceLedgerBackfiller.BackfillCurrentTenantAsync();
        }

        // (6) Çok-şirket güvenlik sınırı geçiş backfill'i: ICompanyOwned'a taşınan SubAccount/Vault'ta
        //     migration'ın Guid.Empty bıraktığı CompanyId'yi parent'tan doldur (idempotent; boş satır
        //     yoksa no-op). Org yapısı tenant-scoped → yalnız tenant'ta.
        if (context.TenantId != null)
        {
            await _companyOwnedBackfiller.BackfillCurrentTenantAsync();
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
