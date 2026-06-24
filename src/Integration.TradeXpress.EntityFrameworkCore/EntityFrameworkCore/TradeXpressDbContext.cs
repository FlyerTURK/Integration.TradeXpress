using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.BlobStoring.Database.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Stones;

namespace Integration.TradeXpress.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class TradeXpressDbContext :
    AbpDbContext<TradeXpressDbContext>,
    ITenantManagementDbContext,
    IIdentityDbContext
{
    /* Add DbSet properties for your Aggregate Roots / Entities here. */
    public DbSet<CurrencyUnit> CurrencyUnits { get; set; } = null!;
    public DbSet<CurrencyUnitMargin> CurrencyUnitMargins { get; set; } = null!;
    public DbSet<ExchangeRate> ExchangeRates { get; set; } = null!;
    public DbSet<Parity> Parities { get; set; } = null!;
    public DbSet<Company> Companies { get; set; } = null!;
    public DbSet<Country> Countries { get; set; } = null!;
    public DbSet<Branch> Branches { get; set; } = null!;
    public DbSet<Vault> Vaults { get; set; } = null!;
    public DbSet<Cash> Cashes { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<Future> Futures { get; set; } = null!;
    public DbSet<Scrap> Scraps { get; set; } = null!;
    public DbSet<Metal> Metals { get; set; } = null!;
    public DbSet<Stone> Stones { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<SubAccount> SubAccounts { get; set; } = null!;
    public DbSet<Voucher> Vouchers { get; set; } = null!;
    public DbSet<VoucherLine> VoucherLines { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Authorization.UserScopedGrant> UserScopedGrants { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Settings.UserGridLayout> UserGridLayouts { get; set; } = null!;


    #region Entities from the modules

    /* Notice: We only implemented IIdentityProDbContext and ISaasDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityProDbContext and ISaasDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    // Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }

    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public TradeXpressDbContext(DbContextOptions<TradeXpressDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureFeatureManagement();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureTenantManagement();
        builder.ConfigureBlobStoring();
        
        /* Configure your own tables/entities inside here — alan-domain extension metotları */

        builder.ConfigureCurrencies();
        builder.ConfigureCompanies();
        builder.ConfigureCountries();
        builder.ConfigureBranches();
        builder.ConfigureVaults();
        builder.ConfigureCashes();
        builder.ConfigureServices();
        builder.ConfigureFutures();
        builder.ConfigureScraps();
        builder.ConfigureMetals();
        builder.ConfigureStones();
        builder.ConfigureAccounts();
        builder.ConfigureVouchers();
        builder.ConfigureUserScopedGrants();
        builder.ConfigureUserGridLayouts();
    }
}
