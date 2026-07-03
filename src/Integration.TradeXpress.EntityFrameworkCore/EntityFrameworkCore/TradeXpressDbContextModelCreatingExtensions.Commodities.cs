using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Cashes;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Emtia/enstrüman mapping'leri: nakit, hizmet, vadeli, hurda, maden, taş, mücevher.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
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
}
