using Integration.TradeXpress.TrendyolShipments;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Integration.TradeXpress.EntityFrameworkCore;

public static class TradeXpressDbContextModelCreatingExtensionsTrendyolShipments
{
    /// <summary>Trendyol kargo referansı — HOST-GLOBAL (IMultiTenant değil; N11ShipmentCompany ile aynı desen).
    /// Kimlik <c>ExternalId</c> (Trendyol'un <c>cargoCompanyId</c>'si) → benzersiz, soft-delete farkındalı filtre.</summary>
    public static void ConfigureTrendyolShipments(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<TrendyolCargoProvider>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "TrendyolCargoProviders", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(TrendyolShipmentConsts.ExternalIdMaxLength);
            b.Property(x => x.Code).IsRequired().HasMaxLength(TrendyolShipmentConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(TrendyolShipmentConsts.NameMaxLength);
            b.Property(x => x.TaxNumber).HasMaxLength(TrendyolShipmentConsts.TaxNumberMaxLength);

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
        });
    }
}
