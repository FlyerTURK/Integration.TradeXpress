using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.TrendyolProducts;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Trendyol ürün listeleme mapping'i — ürün×kanal listelemesi (company-owned). Kategori attribute (id-bazlı)
/// owned-collection → JSON kolonu; kimlik (SalesChannelId, ProductId) soft-delete filtreli benzersiz.</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureTrendyolProducts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<TrendyolProductListing>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "TrendyolProductListings", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CategoryId).IsRequired().HasMaxLength(TrendyolProductConsts.CategoryIdMaxLength);
            b.Property(x => x.CategoryName).HasMaxLength(TrendyolProductConsts.CategoryNameMaxLength);
            b.Property(x => x.BrandId).IsRequired().HasMaxLength(TrendyolProductConsts.BrandIdMaxLength);
            b.Property(x => x.BatchRequestId).HasMaxLength(TrendyolProductConsts.BatchRequestIdMaxLength);
            b.Property(x => x.Status).HasMaxLength(TrendyolProductConsts.StatusMaxLength);
            b.Property(x => x.LastError).HasMaxLength(TrendyolProductConsts.LastErrorMaxLength);
            b.Property(x => x.DimensionalWeight).HasPrecision(18, 3);

            // Kategori attribute değerleri (id-bazlı) → JSON kolonu (owned collection; Trendyol'a push edilir, sorgulanmaz).
            b.OwnsMany(x => x.Attributes, a =>
            {
                a.ToJson();
                a.Property(p => p.CustomValue).HasMaxLength(TrendyolProductConsts.CustomAttributeValueMaxLength);
            });

            // Kimlik: bir ürün bir kanalda TEK listeleme; soft-delete filtreli (silinen yeniden listelenebilsin).
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
