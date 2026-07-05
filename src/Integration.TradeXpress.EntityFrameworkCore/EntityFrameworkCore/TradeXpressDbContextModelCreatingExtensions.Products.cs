using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Ürün/varyant mapping'leri — marketplace-hazır Product çekirdeği (Faz 1, Adım 1).
/// Product = company-owned vitrin; ProductVariant = ürüne bağlı (kod ürün başına tekil, tekil main).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProducts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Product>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Products", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(ProductConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(ProductConsts.DescriptionMaxLength);

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<ProductVariant>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductVariants", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(ProductConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(ProductConsts.DescriptionMaxLength);

            // Varyant kodu ÜRÜN başına tekil (SubAccount = Account başına deseniyle hizalı).
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.Code }).IsUnique();
            // Company güvenlik query-filter'ını hızlandırır (ICompanyOwned).
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            // Ana varyant araması (tek-main invariant).
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.IsMain });
        });
    }
}
