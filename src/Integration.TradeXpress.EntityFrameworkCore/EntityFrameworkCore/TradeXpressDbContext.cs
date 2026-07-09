using System;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
using Volo.Abp.MultiTenancy;
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
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Scheduling;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Jewelries;

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
    public DbSet<AssayOffice> AssayOffices { get; set; } = null!;
    public DbSet<SchedulerAppointment> SchedulerAppointments { get; set; } = null!;
    public DbSet<Cash> Cashes { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    // SalesChannel TPT: soyut taban + somut alt-tipler (N11, Trendyol) — ABP repository'si concrete tip üzerinden.
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelBase> SalesChannels { get; set; } = null!;
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelTrN11> SalesChannelTrN11s { get; set; } = null!;
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelTrTrendyol> SalesChannelTrTrendyols { get; set; } = null!;
    public DbSet<Future> Futures { get; set; } = null!;
    public DbSet<Scrap> Scraps { get; set; } = null!;
    public DbSet<Metal> Metals { get; set; } = null!;
    public DbSet<Stone> Stones { get; set; } = null!;
    public DbSet<Jewelry> Jewelries { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<SubAccount> SubAccounts { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.Product> Products { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductVariant> ProductVariants { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductAttribute> ProductAttributes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductAttributeValue> ProductAttributeValues { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductVariantAttributeValue> ProductVariantAttributeValues { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductVariantRecipeLine> ProductVariantRecipeLines { get; set; } = null!;
    public DbSet<Voucher> Vouchers { get; set; } = null!;
    public DbSet<VoucherLine> VoucherLines { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Vouchers.Balance.BalanceLedgerEntry> BalanceLedgerEntries { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshot> BalanceSheetSnapshots { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Authorization.UserScopedGrant> UserScopedGrants { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Settings.UserGridLayout> UserGridLayouts { get; set; } = null!;
    // N11 kategori taksonomisi — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır).
    public DbSet<Integration.TradeXpress.N11Categories.N11Category> N11Categories { get; set; } = null!;
    // Trendyol kategori taksonomisi — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır).
    public DbSet<Integration.TradeXpress.TrendyolCategories.TrendyolCategory> TrendyolCategories { get; set; } = null!;
    // N11 adres taksonomisi (İl/İlçe) — HOST-GLOBAL. Mahalleler saklanmaz (on-demand).
    public DbSet<Integration.TradeXpress.N11Cities.N11City> N11Cities { get; set; } = null!;
    public DbSet<Integration.TradeXpress.N11Cities.N11District> N11Districts { get; set; } = null!;
    // N11 kargo firmaları — HOST-GLOBAL.
    public DbSet<Integration.TradeXpress.N11Shipments.N11ShipmentCompany> N11ShipmentCompanies { get; set; } = null!;
    // N11 kargo şablonları — per-kanal (company-owned).
    public DbSet<Integration.TradeXpress.N11Shipments.N11ShipmentTemplate> N11ShipmentTemplates { get; set; } = null!;
    // N11 ürün listelemeleri — ürün×kanal (company-owned). DbSet ŞART: ABP default repository'leri DbSet'ten keşfeder.
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11Product> SalesChannelTrN11Products { get; set; } = null!;
    // N11 kanal-özel varyant EKSENİ/DEĞERİ (ERP ProductAttribute/Value klonu; klon-sonra-ayrış).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductAttributeAxis> SalesChannelTrN11ProductAttributeAxes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductAttributeAxisValue> SalesChannelTrN11ProductAttributeAxisValues { get; set; } = null!;
    // N11 kanal-özel varyant override başlığı (fiyat/stok + marj; ayrı tablo).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductVariant> SalesChannelTrN11ProductVariants { get; set; } = null!;
    // N11 kanal-özel varyant reçete satırları (ayrı tablo; ERP reçetesi klonu).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductVariantRecipeLine> SalesChannelTrN11ProductVariantRecipeLines { get; set; } = null!;
    // Trendyol ürün listelemeleri — ürün×kanal (company-owned).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProduct> SalesChannelTrTrendyolProducts { get; set; } = null!;
    // Trendyol kanal-özel varyant override başlığı (fiyat/stok + marj; ayrı tablo).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductVariant> SalesChannelTrTrendyolProductVariants { get; set; } = null!;
    // Trendyol kanal-özel varyant reçete satırları (ayrı tablo; ERP reçetesi klonu).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductVariantRecipeLine> SalesChannelTrTrendyolProductVariantRecipeLines { get; set; } = null!;


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
        builder.ConfigureAssayOffices();
        builder.ConfigureProducts();
        builder.ConfigureSchedulerAppointments();
        builder.ConfigureCashes();
        builder.ConfigureServices();
        builder.ConfigureSalesChannels();
        builder.ConfigureFutures();
        builder.ConfigureScraps();
        builder.ConfigureMetals();
        builder.ConfigureStones();
        builder.ConfigureJewelries();
        builder.ConfigureAccounts();
        builder.ConfigureVouchers();
        builder.ConfigureBalanceLedger();
        builder.ConfigureBalanceSheetSnapshots();
        builder.ConfigureUserScopedGrants();
        builder.ConfigureUserGridLayouts();
        builder.ConfigureN11Categories();
        builder.ConfigureTrendyolCategories();
        builder.ConfigureN11Cities();
        builder.ConfigureN11Shipments();
        builder.ConfigureN11Products();
        builder.ConfigureTrendyolProducts();

        // Kod kolonlarına ordinal (BIN2) collation — YALNIZ SQL Server. C# ToUpperInvariant ile hizalanır,
        // Türkçe İ/i collation kaçağını DB tarafında da kapatır. Sqlite (test) BIN2'yi tanımaz → guard'la atlanır
        // (Sqlite default BINARY zaten ordinal; fonksiyonel olarak aynı benzersizlik).
        if (Database.IsSqlServer())
        {
            builder.ApplyCodeColumnCollations();
        }
    }

    /// <summary>
    /// Company-scoped (<see cref="ICompanyScoped"/>) eklenen kayıtlara,
    /// CompanyId boşsa aktif çalışılan şirketi otomatik basar (ABP'nin TenantId auto-stamp'ının company eşdeğeri).
    /// Çalışılan şirket yoksa null kalır = holding-host.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, System.Threading.CancellationToken cancellationToken = default)
    {
        StampCompanyScoped();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    #region Company-scoped global query filter

    /// <summary>
    /// Company görünürlük filtresi açık mı? ABP <c>IDataFilter</c> anahtarı <see cref="ICompanyScoped"/>'tur
    /// (IMultiTenant/ISoftDelete deseniyle aynı); konsolide/rapor sorguları
    /// <c>DataFilter.Disable&lt;ICompanyScoped&gt;()</c> ile bilinçli kapatır. Varsayılan: AÇIK.
    /// </summary>
    protected virtual bool IsCompanyScopedFilterEnabled
    {
        get
        {
            var dataFilter = LazyServiceProvider?.LazyGetService<IDataFilter>();
            return dataFilter?.IsEnabled<ICompanyScoped>() ?? false;
        }
    }

    /// <summary>Aktif (working) şirket — Blazor circuit'inde working-context köprüsünden gelir; API/host'ta null.</summary>
    protected virtual Guid? CurrentCompanyId
    {
        get
        {
            var currentCompany = LazyServiceProvider?.LazyGetService<ICurrentCompany>();
            return currentCompany?.Id;
        }
    }

    protected override bool ShouldFilterEntity<TEntity>(IMutableEntityType entityType)
    {
        // İki company-marker'ı da aynı filtre kapsamına alır: ICompanyScoped (görünüm, CompanyId=Guid?) +
        // ICompanyOwned (güvenlik sınırı, CompanyId=Guid). Anahtar tek: IDataFilter<ICompanyScoped>.
        if (typeof(ICompanyScoped).IsAssignableFrom(typeof(TEntity))
            || typeof(ICompanyOwned).IsAssignableFrom(typeof(TEntity)))
        {
            return true;
        }

        return base.ShouldFilterEntity<TEntity>(entityType);
    }

    /// <summary>
    /// <see cref="ICompanyScoped"/> entity'lere company görünürlük filtresini ABP'nin soft-delete/multi-tenant
    /// filtreleriyle BİRLEŞTİREREK ekler. Semantik <c>CompanyScopedQueryable.CompanyVisiblePredicate</c> ile
    /// birebir: host kaydı (TenantId=null) HERKESE görünür; working şirket yokken (CurrentCompanyId=null)
    /// konsolide = kısıt yok; CompanyId=null (holding-host) herkese görünür; dolu CompanyId yalnız kendi
    /// şirketine. Tenant boyutu ABP'nin IMultiTenant filtresinin işi — burada tekrar edilmez (host-muafiyet
    /// istisnası hariç). Elle <c>WhereCompanyVisible</c> çağrıları yerinde kalır (çift katman zararsız);
    /// bu filtre unutulan çağrı sınıfını YAPISAL kapatan güvenlik ağıdır.
    /// </summary>
    protected override Expression<Func<TEntity, bool>>? CreateFilterExpression<TEntity>(
        ModelBuilder modelBuilder,
        EntityTypeBuilder<TEntity> entityTypeBuilder)
        where TEntity : class
    {
        var expression = base.CreateFilterExpression<TEntity>(modelBuilder, entityTypeBuilder);

        if (typeof(ICompanyScoped).IsAssignableFrom(typeof(TEntity)))
        {
            var companyFilter = CreateCompanyScopedFilterExpression<TEntity>();
            expression = expression == null
                ? companyFilter
                : QueryFilterExpressionHelper.CombineExpressions(expression, companyFilter);
        }
        else if (typeof(ICompanyOwned).IsAssignableFrom(typeof(TEntity)))
        {
            // ICompanyScoped ve ICompanyOwned kasıtla birbirini dışlar (Guid? vs Guid); else-if güvenli.
            var companyFilter = CreateCompanyOwnedFilterExpression<TEntity>();
            expression = expression == null
                ? companyFilter
                : QueryFilterExpressionHelper.CombineExpressions(expression, companyFilter);
        }

        return expression;
    }

    /// <summary>Filtre durumu compiled-query cache anahtarına girer (ABP custom-filter gereği).</summary>
    public override string GetCompiledQueryCacheKey()
    {
        return $"{base.GetCompiledQueryCacheKey()}:{IsCompanyScopedFilterEnabled}:{CurrentCompanyId?.ToString() ?? "Null"}";
    }

    private Expression<Func<TEntity, bool>> CreateCompanyScopedFilterExpression<TEntity>()
        where TEntity : class
    {
        // IMultiTenant + ICompanyScoped (mevcut tüm implementasyonlar): host kaydı company filtresinden muaf.
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e =>
                !IsCompanyScopedFilterEnabled
                || CurrentCompanyId == null
                || EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == null
                || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == null
                || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == CurrentCompanyId;
        }

        // Salt ICompanyScoped (tenant'sız — bugün yok, ileriye dönük): host-muafiyet kolu düşer.
        return e =>
            !IsCompanyScopedFilterEnabled
            || CurrentCompanyId == null
            || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == null
            || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == CurrentCompanyId;
    }

    /// <summary>
    /// <see cref="ICompanyOwned"/> (güvenlik sınırı) filtresi: kayıt DAİMA tek şirkete aittir,
    /// "holding-host (null)" görünür kolu YOKTUR (<see cref="ICompanyScoped"/>'tan tek yapısal fark).
    /// CompanyId non-nullable <see cref="Guid"/> → karşılaştırma için <c>(Guid?)</c>'a yükseltilir.
    /// Host kaydı (TenantId=null) yine muaf (finansal çekirdek per-tenant ama host seed/rapor kırılmasın).
    /// Konsolide (CurrentCompanyId=null) PERMISSIVE.
    /// </summary>
    private Expression<Func<TEntity, bool>> CreateCompanyOwnedFilterExpression<TEntity>()
        where TEntity : class
    {
        // Mevcut tüm ICompanyOwned implementasyonları IMultiTenant (Account/Voucher ailesi): host muaf.
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e =>
                !IsCompanyScopedFilterEnabled
                || CurrentCompanyId == null
                || EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == null
                || (Guid?)EF.Property<Guid>(e, nameof(ICompanyOwned.CompanyId)) == CurrentCompanyId;
        }

        // Salt ICompanyOwned (tenant'sız — ileriye dönük): host-muafiyet kolu düşer.
        return e =>
            !IsCompanyScopedFilterEnabled
            || CurrentCompanyId == null
            || (Guid?)EF.Property<Guid>(e, nameof(ICompanyOwned.CompanyId)) == CurrentCompanyId;
    }

    #endregion

    private void StampCompanyScoped()
    {
        var current = LazyServiceProvider?.LazyGetService<ICurrentCompany>();
        var companyId = current?.Id;
        if (companyId == null)
        {
            return; // çalışılan şirket yok → holding-host (null) bırak
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added
                && entry.Entity is ICompanyScoped { CompanyId: null })
            {
                entry.Property(nameof(ICompanyScoped.CompanyId)).CurrentValue = companyId;
            }
        }
    }
}
