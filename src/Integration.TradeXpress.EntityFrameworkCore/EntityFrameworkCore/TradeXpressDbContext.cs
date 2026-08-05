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
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.VariantTemplates;
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
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.SpecialCodes;
using Integration.TradeXpress.Attachments;

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
    public DbSet<AddOn> AddOns { get; set; } = null!;
    // Çekirdek kargo firması — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır; Geography deseni).
    public DbSet<VariantTemplate> VariantTemplates { get; set; } = null!;
    public DbSet<SchedulerAppointment> SchedulerAppointments { get; set; } = null!;
    public DbSet<Cash> Cashes { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    // SalesChannel TPT: soyut taban + somut alt-tipler (N11, Trendyol, Etsy) — ABP repository'si concrete tip üzerinden.
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelBase> SalesChannels { get; set; } = null!;
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelTrN11> SalesChannelTrN11s { get; set; } = null!;
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelTrTrendyol> SalesChannelTrTrendyols { get; set; } = null!;
    public DbSet<Integration.TradeXpress.SalesChannels.SalesChannelEtsy> SalesChannelEtsys { get; set; } = null!;
    public DbSet<Future> Futures { get; set; } = null!;
    public DbSet<Scrap> Scraps { get; set; } = null!;
    public DbSet<Metal> Metals { get; set; } = null!;
    public DbSet<Stone> Stones { get; set; } = null!;
    public DbSet<Jewelry> Jewelries { get; set; } = null!;
    public DbSet<Good> Goods { get; set; } = null!;
    public DbSet<GoodSupplier> GoodSuppliers { get; set; } = null!;
    public DbSet<GoodVariantDetail> GoodVariantDetails { get; set; } = null!;
    public DbSet<Variants.EntityAttribute> EntityAttributes { get; set; } = null!;
    public DbSet<Variants.EntityAttributeValue> EntityAttributeValues { get; set; } = null!;
    public DbSet<Variants.EntityVariant> EntityVariants { get; set; } = null!;
    public DbSet<Variants.EntityVariantAttributeValue> EntityVariantAttributeValues { get; set; } = null!;
    // Çekirdek ürün kategorisi (company-owned ağaç) + nitelik/değer — pazaryeri kategorilerine eşleştirme hedefi.
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategory> ProductCategories { get; set; } = null!;
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategoryAttribute> ProductCategoryAttributes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategoryAttributeValue> ProductCategoryAttributeValues { get; set; } = null!;
    // Kategori ↔ satış kanalı kategorisi eşleştirmesi — kanal kategorisi ve komisyonu bu köprüden çözülür.
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategoryChannelMapping> ProductCategoryChannelMappings { get; set; } = null!;
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategoryChannelAttributeMapping> ProductCategoryChannelAttributeMappings { get; set; } = null!;
    public DbSet<Integration.TradeXpress.ProductCategories.ProductCategoryChannelAttributeValueMapping> ProductCategoryChannelAttributeValueMappings { get; set; } = null!;
    // Reçete şablonu ("orta reçete": hizmet/paketleme/kargo/sigorta/yarı mamul demeti) + satırları.
    public DbSet<Integration.TradeXpress.RecipeTemplates.RecipeTemplate> RecipeTemplates { get; set; } = null!;
    public DbSet<Integration.TradeXpress.RecipeTemplates.RecipeTemplateLine> RecipeTemplateLines { get; set; } = null!;
    public DbSet<SpecialCode> SpecialCodes { get; set; } = null!;
    public DbSet<EntityDocument> EntityDocuments { get; set; } = null!;
    public DbSet<EntityNote> EntityNotes { get; set; } = null!;
    public DbSet<Media> MediaItems { get; set; } = null!;
    public DbSet<EntityMediaLink> EntityMediaLinks { get; set; } = null!;
    public DbSet<MediaFolder> MediaFolders { get; set; } = null!;
    public DbSet<Account> Accounts { get; set; } = null!;
    public DbSet<SubAccount> SubAccounts { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.Product> Products { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductVariantRecipeLine> ProductVariantRecipeLines { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductVariantDetail> ProductVariantDetails { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Products.ProductSpecification> ProductSpecifications { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Metals.MetalVariantDetail> MetalVariantDetails { get; set; } = null!;
    public DbSet<Voucher> Vouchers { get; set; } = null!;
    public DbSet<VoucherLine> VoucherLines { get; set; } = null!;
    public DbSet<VoucherLineHistory> VoucherLineHistories { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Vouchers.Balance.BalanceLedgerEntry> BalanceLedgerEntries { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshot> BalanceSheetSnapshots { get; set; } = null!;
    // Teyit (organizasyon-içi karşılıklı ayna onayı) — company-owned staging kaydı.
    public DbSet<Integration.TradeXpress.Confirmations.Confirmation> Confirmations { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Authorization.UserScopedGrant> UserScopedGrants { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Settings.UserGridLayout> UserGridLayouts { get; set; } = null!;
    // N11 kategori taksonomisi — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır).
    public DbSet<Integration.TradeXpress.N11Categories.N11Category> N11Categories { get; set; } = null!;
    // Trendyol kategori taksonomisi — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır).
    public DbSet<Integration.TradeXpress.TrendyolCategories.TrendyolCategory> TrendyolCategories { get; set; } = null!;
    // Trendyol marka write-through cache'i — HOST-GLOBAL (tam sync YOK; yalnız seçilen/ithal markalar).
    public DbSet<Integration.TradeXpress.TrendyolBrands.TrendyolBrand> TrendyolBrands { get; set; } = null!;
    // Etsy seller taxonomy — HOST-GLOBAL (IMultiTenant değil; tüm tenant'lar paylaşır).
    public DbSet<Integration.TradeXpress.EtsyTaxonomies.EtsyTaxonomy> EtsyTaxonomies { get; set; } = null!;
    // N11 adres taksonomisi (İl/İlçe) — HOST-GLOBAL. Mahalleler saklanmaz (on-demand).
    public DbSet<Integration.TradeXpress.N11Cities.N11City> N11Cities { get; set; } = null!;
    public DbSet<Integration.TradeXpress.N11Cities.N11District> N11Districts { get; set; } = null!;
    // Çekirdek coğrafya (idari alan/yerellik/alt-yerellik — ISO 3166-2 hizalı) — HOST-seed, IMultiTenant.
    public DbSet<Integration.TradeXpress.Geography.AdministrativeArea> AdministrativeAreas { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Geography.Locality> Localities { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Geography.SubLocality> SubLocalities { get; set; } = null!;
    // N11 kargo firmaları — HOST-GLOBAL.
    public DbSet<Integration.TradeXpress.N11Shipments.N11ShipmentCompany> N11ShipmentCompanies { get; set; } = null!;

    /// <summary>Trendyol kargo firmaları — HOST-GLOBAL referans. Canlı uçtan DEĞİL resmî statik listeden
    /// SEED edilir (Trendyol'da getProviders diye bir HTTP ucu yoktur; bkz. TrendyolCargoProviderSeeder).</summary>
    public DbSet<Integration.TradeXpress.TrendyolShipments.TrendyolCargoProvider> TrendyolCargoProviders { get; set; } = null!;
    // N11 kargo şablonları — per-kanal (company-owned).
    public DbSet<Integration.TradeXpress.N11Shipments.N11ShipmentTemplate> N11ShipmentTemplates { get; set; } = null!;
    // Pazaryerinin YAYIMLADIĞI anlaşmalı kargo tarifesi (desi fiyat tablosu) — HOST-GLOBAL, yürürlük tarihli.
    public DbSet<Integration.TradeXpress.MarketplaceShipmentTariffs.MarketplaceShipmentTariff> MarketplaceShipmentTariffs { get; set; } = null!;
    // N11 ürün listelemeleri — ürün×kanal (company-owned). DbSet ŞART: ABP default repository'leri DbSet'ten keşfeder.
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11Product> SalesChannelTrN11Products { get; set; } = null!;
    // N11 kanal-özel varyant EKSENİ/DEĞERİ (ERP ProductAttribute/Value klonu; klon-sonra-ayrış).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductAttribute> SalesChannelTrN11ProductAttributes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductAttributeValue> SalesChannelTrN11ProductAttributeValues { get; set; } = null!;
    // N11 kanal-özel varyant override başlığı (fiyat/stok + marj; ayrı tablo).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductStockItem> SalesChannelTrN11ProductStockItems { get; set; } = null!;
    // N11 kanal-özel varyant reçete satırları (ayrı tablo; ERP reçetesi klonu).
    public DbSet<Integration.TradeXpress.N11Products.SalesChannelTrN11ProductStockItemRecipeLine> SalesChannelTrN11ProductStockItemRecipeLines { get; set; } = null!;
    // Etsy ürün listelemeleri — ürün×kanal (company-owned). N11 ikizi.
    public DbSet<Integration.TradeXpress.EtsyProducts.SalesChannelEtsyProduct> SalesChannelEtsyProducts { get; set; } = null!;
    // Etsy kanal-özel varyant EKSENİ/DEĞERİ (ERP ProductAttribute/Value klonu; klon-sonra-ayrış).
    public DbSet<Integration.TradeXpress.EtsyProducts.SalesChannelEtsyProductAttribute> SalesChannelEtsyProductAttributes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.EtsyProducts.SalesChannelEtsyProductAttributeValue> SalesChannelEtsyProductAttributeValues { get; set; } = null!;
    // Etsy kanal-özel varyant override başlığı (fiyat/stok + marj; ayrı tablo).
    public DbSet<Integration.TradeXpress.EtsyProducts.SalesChannelEtsyProductStockItem> SalesChannelEtsyProductStockItems { get; set; } = null!;
    // Etsy kanal-özel varyant reçete satırları (ayrı tablo; ERP reçetesi klonu).
    public DbSet<Integration.TradeXpress.EtsyProducts.SalesChannelEtsyProductStockItemRecipeLine> SalesChannelEtsyProductStockItemRecipeLines { get; set; } = null!;
    // Trendyol ürün listelemeleri — ürün×kanal (company-owned).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProduct> SalesChannelTrTrendyolProducts { get; set; } = null!;
    // Trendyol kanal-özel varyant EKSENİ/DEĞERİ (ERP ProductAttribute/Value klonu; klon-sonra-ayrış).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductAttribute> SalesChannelTrTrendyolProductAttributes { get; set; } = null!;
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductAttributeValue> SalesChannelTrTrendyolProductAttributeValues { get; set; } = null!;
    // Trendyol kanal-özel varyant override başlığı (fiyat/stok + marj; ayrı tablo).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductStockItem> SalesChannelTrTrendyolProductStockItems { get; set; } = null!;
    // Trendyol kanal-özel varyant reçete satırları (ayrı tablo; ERP reçetesi klonu).
    public DbSet<Integration.TradeXpress.TrendyolProducts.SalesChannelTrTrendyolProductStockItemRecipeLine> SalesChannelTrTrendyolProductStockItemRecipeLines { get; set; } = null!;
    // Muadil grubu — company-owned başlık + sıralı emtia satırları (ayrı aggregate, id-only referans).
    public DbSet<Integration.TradeXpress.Substitutions.SubstitutionGroup> SubstitutionGroups { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Substitutions.SubstitutionGroupItem> SubstitutionGroupItems { get; set; } = null!;
    // Sipariş (NÖTR) — tüm satış kanallarının siparişleri tek tabloda (company-owned); salt-okuma çekim (O0).
    public DbSet<Integration.TradeXpress.Orders.Order> Orders { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Orders.OrderLine> OrderLines { get; set; } = null!;
    // Sipariş YEREL/OPERASYONEL katmanı (O1) — resync'ten bağımsız düzeltme/versiyon bağı (bkz. entity XML doc).
    public DbSet<Integration.TradeXpress.Orders.OrderOperationalData> OrderOperationalData { get; set; } = null!;
    public DbSet<Integration.TradeXpress.Orders.OrderLineOperationalData> OrderLineOperationalData { get; set; } = null!;
    // Müşteri sorusu (NÖTR) — tüm satış kanallarının ürün soruları tek tabloda (company-owned); salt-okuma çekim,
    // cevap yerelde yazılır ama pazaryerine GÖNDERİLMEZ (push ayrı onayla açılacak).
    public DbSet<Integration.TradeXpress.ChannelQuestions.ChannelQuestion> ChannelQuestions { get; set; } = null!;
    // Soru senkron DEFTERİ — kanal başına tek satır; dakikada-tek-adım çekiminin kalıcı ilerlemesi (seed ayı,
    // sayfa imleci, son tazeleme). Uygulama yeniden başlayınca seed baştan başlamasın diye DB'de tutulur.
    public DbSet<Integration.TradeXpress.ChannelQuestions.ChannelQuestionSyncState> ChannelQuestionSyncStates { get; set; } = null!;


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
        builder.ConfigureGeography();
        builder.ConfigureBranches();
        builder.ConfigureVaults();
        builder.ConfigureAssayOffices();
        builder.ConfigureAddOns();
        builder.ConfigureVariantTemplates();
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
        builder.ConfigureGoods();
        builder.ConfigureGoodSuppliers();
        builder.ConfigureGoodVariantDetails();
        builder.ConfigureProductVariantDetails();
        builder.ConfigureProductSpecifications();
        builder.ConfigureMetalVariantDetails();
        builder.ConfigureEntityVariants();
        builder.ConfigureProductCategories();
        builder.ConfigureProductCategoryChannelMappings();
        builder.ConfigureProductCategoryChannelAttributeMappings();
        builder.ConfigureProductCategoryChannelAttributeValueMappings();
        builder.ConfigureRecipeTemplates();
        builder.ConfigureSpecialCodes();
        builder.ConfigureEntityDocuments();
        builder.ConfigureEntityNotes();
        builder.ConfigureMedia();
        builder.ConfigureAccounts();
        builder.ConfigureVouchers();
        builder.ConfigureVoucherLineHistories();
        builder.ConfigureBalanceLedger();
        builder.ConfigureBalanceSheetSnapshots();
        builder.ConfigureConfirmations();
        builder.ConfigureUserScopedGrants();
        builder.ConfigureUserGridLayouts();
        builder.ConfigureN11Categories();
        builder.ConfigureTrendyolCategories();
        builder.ConfigureTrendyolBrands();
        builder.ConfigureEtsyTaxonomies();
        builder.ConfigureN11Cities();
        builder.ConfigureN11Shipments();
        builder.ConfigureTrendyolShipments();
        builder.ConfigureMarketplaceShipmentTariffs();
        builder.ConfigureN11Products();
        builder.ConfigureTrendyolProducts();
        builder.ConfigureEtsyProducts();
        builder.ConfigureSubstitutions();
        builder.ConfigureOrders();
        builder.ConfigureChannelQuestions();
        builder.ConfigureChannelQuestionSyncStates();

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
        // SENTINEL KORUMASI (CurrentCompanyId == Guid.Empty = "hiç şirket yetkisi yok"): eşitlik kolu
        // KAPATILIR — aksi halde CompanyId'si Guid.Empty olan satır tam da YETKİSİZ kullanıcıya görünür.
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e =>
                !IsCompanyScopedFilterEnabled
                || CurrentCompanyId == null
                || EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == null
                || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == null
                || (CurrentCompanyId != Guid.Empty
                    && EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == CurrentCompanyId);
        }

        // Salt ICompanyScoped (tenant'sız — bugün yok, ileriye dönük): host-muafiyet kolu düşer.
        return e =>
            !IsCompanyScopedFilterEnabled
            || CurrentCompanyId == null
            || EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == null
            || (CurrentCompanyId != Guid.Empty
                && EF.Property<Guid?>(e, nameof(ICompanyScoped.CompanyId)) == CurrentCompanyId);
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
        // SENTINEL KORUMASI — kritik: <c>WorkingCompanyScope</c> hiç şirket yetkisi olmayan kullanıcıya
        // Guid.Empty sentinel'i verir ve bunun güvenli olması "hiçbir gerçek CompanyId Guid.Empty değil"
        // varsayımına dayanıyordu. Görev #4 migration'ları CompanyId'yi <c>defaultValue: Guid.Empty</c> ile
        // eklediğinden bu varsayım ÇÜRÜDÜ: sahiplendirilmemiş (yetim) satır artık Guid.Empty taşıyabilir ve
        // eşitlik kolu onu tam da YETKİSİZ kullanıcıya görünür kılardı — filtrenin TERSİNE dönmesi.
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(TEntity)))
        {
            return e =>
                !IsCompanyScopedFilterEnabled
                || CurrentCompanyId == null
                || EF.Property<Guid?>(e, nameof(IMultiTenant.TenantId)) == null
                || (CurrentCompanyId != Guid.Empty
                    && (Guid?)EF.Property<Guid>(e, nameof(ICompanyOwned.CompanyId)) == CurrentCompanyId);
        }

        // Salt ICompanyOwned (tenant'sız — ileriye dönük): host-muafiyet kolu düşer.
        return e =>
            !IsCompanyScopedFilterEnabled
            || CurrentCompanyId == null
            || (CurrentCompanyId != Guid.Empty
                && (Guid?)EF.Property<Guid>(e, nameof(ICompanyOwned.CompanyId)) == CurrentCompanyId);
    }

    #endregion

    private void StampCompanyScoped()
    {
        var current = LazyServiceProvider?.LazyGetService<ICurrentCompany>();
        var companyId = current?.Id;
        if (companyId == null || companyId == Guid.Empty)
        {
            // Şirket yok → holding-host (null) bırak. Guid.Empty ise bu bir SENTINEL'dir ("hiç şirket
            // yetkisi yok"), gerçek bir şirket DEĞİL — damgalamak sahte sahipli, yalnız yetkisiz
            // kullanıcıya görünen bir kayıt üretirdi (sentinel filtre korumasının simetriği).
            return;
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
