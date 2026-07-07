using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.N11Shipments;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>N11 kargo firması mapping'i — <b>host-global</b> (IMultiTenant değil). ExternalId global benzersiz (IsDeleted filtreli).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Shipments(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<N11ShipmentCompany>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11ShipmentCompanies", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(N11ShipmentConsts.ExternalIdMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(N11ShipmentConsts.NameMaxLength);
            b.Property(x => x.ShortName).IsRequired().HasMaxLength(N11ShipmentConsts.ShortNameMaxLength);
            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
        });
    }
}
