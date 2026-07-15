using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Product varyant satış-fiyatı UZANTISI mapping'i — jenerik EntityVariant'ın Product facet'i (1:1, EntityVariantId).
/// Satış/liste fiyatı varyant seviyesinde (marketplace). id-only bağ (sert FK yok; cascade servis-katmanında).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductVariantDetails(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductVariantDetail>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductVariantDetails", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.SalePrice).HasPrecision(ProductConsts.SalePricePrecision, ProductConsts.SalePriceScale);

            // 1:1 — jenerik varyant başına tek Product detayı.
            b.HasIndex(x => new { x.TenantId, x.EntityVariantId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
