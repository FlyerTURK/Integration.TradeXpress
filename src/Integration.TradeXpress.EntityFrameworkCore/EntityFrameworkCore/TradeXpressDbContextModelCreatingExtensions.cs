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
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Accounts;
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

            // Append-only: tenant+birim başına ÇOK satır (marj geçmişi). Güncel = en son
            // CreationTime. Unique YOK; bu index "en son marj" sorgusunu hızlandırır.
            b.HasIndex(x => new { x.TenantId, x.CurrencyUnitId, x.CreationTime });

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
            b.HasOne(x => x.FollowingUnit)
                .WithMany()
                .HasForeignKey(x => x.FollowingUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.TenantId, x.FollowingUnitId });
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
            b.HasOne(x => x.BalanceCurrencyUnit)
                .WithMany()
                .HasForeignKey(x => x.BalanceCurrencyUnitId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(x => x.LimitUnit)
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

    // MarginSetting owned mapping — her iki alanı (Type enum + Value) explicit map eder.
    private static void ConfigureMargin<TOwner>(OwnedNavigationBuilder<TOwner, MarginSetting> o)
        where TOwner : class
    {
        o.Property(p => p.Type);
        o.Property(p => p.Value).HasPrecision(RatePrecision, RateScale);
    }
}
