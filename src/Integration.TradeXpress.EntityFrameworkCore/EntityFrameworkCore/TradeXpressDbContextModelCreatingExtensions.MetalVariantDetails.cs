using Integration.TradeXpress.Metals;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Integration.TradeXpress.EntityFrameworkCore;

public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureMetalVariantDetails(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<MetalVariantDetail>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "MetalVariantDetails", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntryLabor).HasPrecision(MetalConsts.DecimalPrecision, MetalConsts.DecimalScale);
            b.Property(x => x.ExitLabor).HasPrecision(MetalConsts.DecimalPrecision, MetalConsts.DecimalScale);

            // Jenerik varyanta FK
            b.HasOne<Variants.EntityVariant>().WithMany()
                .HasForeignKey(x => x.EntityVariantId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.EntityVariantId }).IsUnique(); // 1:1 detay tablosu
        });
    }
}
