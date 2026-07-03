using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Financials.ExchangeRates;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Scheduling;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Settings;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Entity mapping'leri OnModelCreating'i şişirmeden, alan-domain bazında extension
/// metotlarında toplar (ABP konvansiyonu). DbContext yalnız <c>builder.ConfigureX()</c> çağırır.
/// </summary>
public static class TradeXpressDbContextModelCreatingExtensions
{
    private const int RatePrecision = 18;
    private const int RateScale = 5;

    public static void ConfigureCurrencies(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<CurrencyUnit>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "CurrencyUnits", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CurrencyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CurrencyConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(CurrencyConsts.DescriptionMaxLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            // Alış/satış marjı CurrencyUnit'te DEĞİL (per-tenant CurrencyUnitMargin'de).
            // Yalnız yapısal/global FollowingMargin owned olarak burada.
            // Type (enum) EXPLICIT map edilmeli — convention get-only enum'u atlıyordu.
            b.OwnsOne(x => x.FollowingMargin, ConfigureMargin); // nullable owned
        });

        builder.Entity<CurrencyUnitMargin>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "CurrencyUnitMargins", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Append-only: tenant+company+birim başına ÇOK satır (marj geçmişi). Güncel = en son
            // CreationTime. Unique YOK; bu index "en son marj" sorgusunu hızlandırır. CompanyId:
            // host→null (global taban), tenant→working company (branch bazlı DEĞİL).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.CurrencyUnitId, x.CreationTime });

            b.OwnsOne(x => x.MarginOnBuy,  ConfigureMargin);
            b.OwnsOne(x => x.MarginOnSell, ConfigureMargin);
        });

        builder.Entity<ExchangeRate>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ExchangeRates", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.MarketPriceOnBuy).HasPrecision(RatePrecision, RateScale);
            b.Property(x => x.MarketPriceOnSell).HasPrecision(RatePrecision, RateScale);
            b.Property(x => x.Source).IsRequired().HasMaxLength(CurrencyConsts.RateSourceMaxLength);
            // Pencere başına birim başına tek satır (worker idempotency backstop).
            b.HasIndex(x => new { x.TenantId, x.CurrencyUnitId, x.RateDate }).IsUnique();

            b.OwnsOne(x => x.AppliedMarginOnBuy,  ConfigureMargin);
            b.OwnsOne(x => x.AppliedMarginOnSell, ConfigureMargin);
        });

        builder.Entity<Parity>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Parities", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Çift tanımı; oran saklanmaz. Yön-bağımsız PairKey ile benzersizlik: USDTRY varken TRYUSD
            // eklenemez ve eşzamanlı yarış da DB tarafından kapanır (app kontrolü tek başına yetmez).
            b.Property(x => x.PairKey).IsRequired().HasMaxLength(72);
            b.HasIndex(x => new { x.TenantId, x.PairKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.IsActive });
        });
    }

    public static void ConfigureCompanies(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Company>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Companies", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CompanyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CompanyConsts.NameMaxLength);
            b.Property(x => x.CountryCode).IsRequired().HasMaxLength(CompanyConsts.CountryCodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(CompanyConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CountryCode });
            b.HasIndex(x => new { x.TenantId, x.IsHeadquarters });
        });
    }

    public static void ConfigureCountries(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Country>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Countries", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CountryConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CountryConsts.NameMaxLength);
            b.Property(x => x.DefaultCurrencyCode).HasMaxLength(CurrencyConsts.CodeMaxLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
    }

    public static void ConfigureBranches(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Branch>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Branches", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(BranchConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(BranchConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(BranchConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.IsHeadquarters });
        });
    }

    public static void ConfigureVaults(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Vault>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Vaults", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(VaultConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(VaultConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(VaultConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.BranchId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BranchId });
            b.HasIndex(x => new { x.TenantId, x.BranchId, x.IsDefault });
        });
    }

    public static void ConfigureAssayOffices(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<AssayOffice>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "AssayOffices", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(AssayOfficeConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AssayOfficeConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(AssayOfficeConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }

    public static void ConfigureSchedulerAppointments(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<SchedulerAppointment>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SchedulerAppointments", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Subject).IsRequired().HasMaxLength(SchedulerAppointmentConsts.SubjectMaxLength);
            b.Property(x => x.Description).HasMaxLength(SchedulerAppointmentConsts.DescriptionMaxLength);
            b.Property(x => x.Location).HasMaxLength(SchedulerAppointmentConsts.LocationMaxLength);
            b.Property(x => x.RecurrenceInfo).HasMaxLength(SchedulerAppointmentConsts.RecurrenceInfoMaxLength);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.StartTime });
        });
    }

    public static void ConfigureCashes(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Cash>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Cashes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CashConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CashConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(CashConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            // Takip edilen para birimi (cins) — ZORUNLU. Takip eden Cash varken birim silinemez (Restrict).
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>()
                .WithMany()
                .HasForeignKey(x => x.FollowingUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.TenantId, x.FollowingUnitId });
        });
    }

    public static void ConfigureServices(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Services.Service>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Services", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Services.ServiceConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Services.ServiceConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Services.ServiceConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
    }

    public static void ConfigureFutures(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Futures.Future>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Futures", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Futures.FutureConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Futures.FutureConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Futures.FutureConsts.DescriptionMaxLength);
            b.Property(x => x.FollowingFactor).HasPrecision(
                Integration.TradeXpress.Futures.FutureConsts.FactorPrecision,
                Integration.TradeXpress.Futures.FutureConsts.FactorScale);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany()
                .HasForeignKey(x => x.FollowingUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.FollowingUnitId });
        });
    }

    public static void ConfigureScraps(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Scraps.Scrap>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Scraps", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Scraps.ScrapConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Scraps.ScrapConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Scraps.ScrapConsts.DescriptionMaxLength);
            b.Property(x => x.Factor).HasPrecision(
                Integration.TradeXpress.Scraps.ScrapConsts.FactorPrecision,
                Integration.TradeXpress.Scraps.ScrapConsts.FactorScale);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany()
                .HasForeignKey(x => x.FollowingUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.FollowingUnitId });
        });
    }

    public static void ConfigureMetals(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Metals.Metal>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Metals", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Metals.MetalConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Metals.MetalConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Metals.MetalConsts.DescriptionMaxLength);
            b.Property(x => x.Barcode).HasMaxLength(Integration.TradeXpress.Metals.MetalConsts.BarcodeMaxLength);
            b.Property(x => x.Factor).HasPrecision(
                Integration.TradeXpress.Metals.MetalConsts.DecimalPrecision, Integration.TradeXpress.Metals.MetalConsts.DecimalScale);
            b.Property(x => x.StableQuantity).HasPrecision(
                Integration.TradeXpress.Metals.MetalConsts.DecimalPrecision, Integration.TradeXpress.Metals.MetalConsts.DecimalScale);
            b.Property(x => x.EntryLabor).HasPrecision(
                Integration.TradeXpress.Metals.MetalConsts.DecimalPrecision, Integration.TradeXpress.Metals.MetalConsts.DecimalScale);
            b.Property(x => x.ExitLabor).HasPrecision(
                Integration.TradeXpress.Metals.MetalConsts.DecimalPrecision, Integration.TradeXpress.Metals.MetalConsts.DecimalScale);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany()
                .HasForeignKey(x => x.FollowingUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.FollowingUnitId });
        });
    }

    public static void ConfigureStones(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Stones.Stone>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Stones", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Stones.StoneConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Stones.StoneConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Stones.StoneConsts.DescriptionMaxLength);
            foreach (var p in new[] { nameof(Integration.TradeXpress.Stones.Stone.StoneKind), nameof(Integration.TradeXpress.Stones.Stone.StoneType),
                nameof(Integration.TradeXpress.Stones.Stone.Color), nameof(Integration.TradeXpress.Stones.Stone.Cut),
                nameof(Integration.TradeXpress.Stones.Stone.Clarity), nameof(Integration.TradeXpress.Stones.Stone.Sieve),
                nameof(Integration.TradeXpress.Stones.Stone.Category), nameof(Integration.TradeXpress.Stones.Stone.GroupCode) })
                b.Property(p).HasMaxLength(Integration.TradeXpress.Stones.StoneConsts.AttributeMaxLength);
            b.Property(x => x.EntryPrice).HasPrecision(Integration.TradeXpress.Stones.StoneConsts.PricePrecision, Integration.TradeXpress.Stones.StoneConsts.PriceScale);
            b.Property(x => x.ExitPrice).HasPrecision(Integration.TradeXpress.Stones.StoneConsts.PricePrecision, Integration.TradeXpress.Stones.StoneConsts.PriceScale);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany().HasForeignKey(x => x.EntryPriceUnitId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany().HasForeignKey(x => x.ExitPriceUnitId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ConfigureJewelries(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Jewelries.Jewelry>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Jewelries", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(Integration.TradeXpress.Jewelries.JewelryConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(Integration.TradeXpress.Jewelries.JewelryConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(Integration.TradeXpress.Jewelries.JewelryConsts.DescriptionMaxLength);
            foreach (var p in new[] { nameof(Integration.TradeXpress.Jewelries.Jewelry.Model), nameof(Integration.TradeXpress.Jewelries.Jewelry.Kind),
                nameof(Integration.TradeXpress.Jewelries.Jewelry.Type), nameof(Integration.TradeXpress.Jewelries.Jewelry.Color),
                nameof(Integration.TradeXpress.Jewelries.Jewelry.Category), nameof(Integration.TradeXpress.Jewelries.Jewelry.GroupCode) })
                b.Property(p).HasMaxLength(Integration.TradeXpress.Jewelries.JewelryConsts.AttributeMaxLength);
            b.Property(x => x.EntryPrice).HasPrecision(Integration.TradeXpress.Jewelries.JewelryConsts.PricePrecision, Integration.TradeXpress.Jewelries.JewelryConsts.PriceScale);
            b.Property(x => x.ExitPrice).HasPrecision(Integration.TradeXpress.Jewelries.JewelryConsts.PricePrecision, Integration.TradeXpress.Jewelries.JewelryConsts.PriceScale);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany().HasForeignKey(x => x.EntryPriceUnitId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany().HasForeignKey(x => x.ExitPriceUnitId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ConfigureAccounts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Account>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Accounts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(AccountConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AccountConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(AccountConsts.DescriptionMaxLength);
            b.Property(x => x.Limit).HasPrecision(18, 2);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });

            // Para birimi referansları (cins + limit) — ZORUNLU; hesap varken birim silinemez (Restrict).
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>()
                .WithMany()
                .HasForeignKey(x => x.BalanceCurrencyUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>()
                .WithMany()
                .HasForeignKey(x => x.LimitUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SubAccount>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SubAccounts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(AccountConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(AccountConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(AccountConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.AccountId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BranchId });

            // Parent hesap (ZORUNLU) + şube (OPSİYONEL/nullable) — id-only (nav YOK); referans varken silme engeli (Restrict).
            b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ConfigureUserScopedGrants(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<UserScopedGrant>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "UserScopedGrants", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.PermissionName).HasMaxLength(UserScopedGrantConsts.PermissionNameMaxLength);

            // Bir kullanıcının atamalarını çekmek için (scoped rol/izin listesi).
            b.HasIndex(x => new { x.TenantId, x.UserId });
        });
    }

    public static void ConfigureUserGridLayouts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<UserGridLayout>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "UserGridLayouts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.GridKey).IsRequired().HasMaxLength(UserGridLayoutConsts.GridKeyMaxLength);
            b.Property(x => x.Layout).IsRequired();   // maxlength YOK → nvarchar(max), truncate olmaz

            b.HasIndex(x => new { x.TenantId, x.UserId, x.GridKey }).IsUnique();
        });
    }

    public static void ConfigureVouchers(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Voucher>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Vouchers", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Description).HasMaxLength(VoucherConsts.DescriptionMaxLength);
            b.Property(x => x.VoucherNumber).IsRequired();
            b.Property(x => x.VoucherDate).IsRequired();

            // Fiş numarası şirket bazında tekil.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.VoucherNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.BranchId });
            b.HasIndex(x => new { x.TenantId, x.AccountId });
            // Perf (keşif turu 2, K3): TÜM raporlar CompanyId+VoucherDate, TÜM cari sorguları
            // CompanyId+SubAccountId(+tarih) filtreler — bunlar index'siz company-scan'e düşüyordu.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.VoucherDate });
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.SubAccountId, x.VoucherDate });

            // FK'lar — referans varken kaynak silinemez (Restrict).
            b.HasOne<Companies.Company>().WithMany()
                .HasForeignKey(x => x.CompanyId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branches.Branch>().WithMany()
                .HasForeignKey(x => x.BranchId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Vaults.Vault>().WithMany()
                .HasForeignKey(x => x.VaultId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Accounts.Account>().WithMany()
                .HasForeignKey(x => x.AccountId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Accounts.SubAccount>().WithMany()
                .HasForeignKey(x => x.SubAccountId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Lines).WithOne(l => l.Voucher).HasForeignKey(l => l.VoucherId)
                .IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<VoucherLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "VoucherLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CommodityCode).HasMaxLength(VoucherConsts.CommodityCodeMaxLength);
            b.Property(x => x.PayCommodityCode).HasMaxLength(VoucherConsts.CommodityCodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(VoucherConsts.DescriptionMaxLength);

            // N5: milyem / çarpan / parite / fiyat / miktar
            b.Property(x => x.Quantity).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.Factor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.MarketPrice).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PayUnitRate).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // Yan metal milyem hassasiyeti — 0.008 gibi değerler default (18,2)'de 0.01'e yuvarlanıyordu
            // (canlı bug; AU Factor zaten N5 konfigürlüydü, yan metaller unutulmuştu).
            b.Property(x => x.SilverFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PlatinumFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);
            b.Property(x => x.PalladiumFactor).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // N2: para / has miktarları
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Total).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.PayTotal).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Profit).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);

            b.HasIndex(x => x.VoucherId);

            // Virman ikiz araması: LinkId (legacy RefNo) ile zıt bacak bulunur (güncelle/sil senkronu).
            b.HasIndex(x => x.LinkId);
        });

    }

    /// <summary>
    /// Bakiye ledger'ı (poster çıktısının kalıcı kaydı) — pozisyon raporu bunu GROUP BY/SUM ile okur.
    /// FK YOK: VoucherId mantıksal referans (id-only desen); senkron app-katmanında (BalanceLedgerSynchronizer).
    /// </summary>
    public static void ConfigureBalanceLedger(this ModelBuilder builder)
    {
        builder.Entity<Integration.TradeXpress.Vouchers.Balance.BalanceLedgerEntry>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "BalanceLedgerEntries", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // İşaretli net etki — N2 (Voucher tutarlarıyla aynı hassasiyet).
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);

            // Rapor: scope + birim bazında GROUP BY/SUM (kapsayan index — DB-tarafı toplam hızlı).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.BranchId, x.UnitId });
            // Senkron: voucher bazında sil + yeniden yaz.
            b.HasIndex(x => x.VoucherId);
        });
    }

    /// <summary>
    /// Bilanço snapshot'ları (dondurulmuş kategori×birim satırları) — ERPPRO <c>Bilanco.Bilancolar</c> paritesi.
    /// FK YOK: CompanyId/BranchId/UnitId/BaseUnitId id-only mantıksal referans (ledger deseni). SaveAsync idempotent
    /// (Scope, CompanyId, BranchId, AsOfDate) bazında sil+yeniden yaz; index o sorguyu + gün-serisi okumasını hızlandırır.
    /// </summary>
    public static void ConfigureBalanceSheetSnapshots(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshot>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "BalanceSheetSnapshots", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Category).IsRequired()
                .HasMaxLength(Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshotConsts.CategoryMaxLength);
            b.Property(x => x.BaseCurrencyCode).IsRequired()
                .HasMaxLength(Integration.TradeXpress.Reports.BalanceSheet.BalanceSheetSnapshotConsts.BaseCurrencyCodeMaxLength);

            // N2 (Voucher tutarlarıyla aynı) miktar/net; N5 (kur çaprazı hassasiyeti) donmuş değerleme kuru.
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Net).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.ValuationRate).HasPrecision(VoucherConsts.FactorPrecision, VoucherConsts.FactorScale);

            // SaveAsync sil+yeniden-yaz + gün-serisi okuması: (kapsam + tarih) kapsayan sorgu index'i.
            b.HasIndex(x => new { x.TenantId, x.Scope, x.CompanyId, x.BranchId, x.AsOfDate });
        });
    }

    // MarginSetting owned mapping — her iki alanı (Type enum + Value) explicit map eder.
    private static void ConfigureMargin<TOwner>(OwnedNavigationBuilder<TOwner, MarginSetting> o)
        where TOwner : class
    {
        o.Property(p => p.Type);
        o.Property(p => p.Value).HasPrecision(RatePrecision, RateScale);
    }
}
