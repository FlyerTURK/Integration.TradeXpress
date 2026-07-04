using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Scheduling;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Settings;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Organizasyon/altyapı mapping'leri: şirket, ülke, şube, kasa, ayar evleri,
/// takvim, scoped yetkiler ve kullanıcı grid layout'ları.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureCompanies(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Company>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Companies", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CompanyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CompanyConsts.NameMaxLength);
            // ESKİ string ülke kodu — Country id-only geçişiyle yalnız backfill kaynağı (artık opsiyonel;
            // yeni kayıt yazmaz). CS0618 uyarısı BİLİNÇLİ: mapping backfill sonrası kolonla birlikte kalkacak.
            b.Property(x => x.CountryCode).HasMaxLength(CompanyConsts.CountryCodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(CompanyConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CountryId });
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
            // ESKİ string birim kodu — id-only geçişiyle yalnız backfill kaynağı (opsiyonel; yeni kayıt yazmaz).
            // CS0618 uyarısı BİLİNÇLİ: mapping backfill sonrası kolonla birlikte kalkacak.
            b.Property(x => x.DefaultCurrencyCode).HasMaxLength(CurrencyConsts.CodeMaxLength);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.DefaultCurrencyUnitId);
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

            // Çok-şirket güvenlik sınırı (ICompanyOwned): company query-filter'ı hızlandırır (Account deseniyle hizalı).
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
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
}
