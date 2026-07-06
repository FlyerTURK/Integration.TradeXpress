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

        builder.Entity<ProductAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductAttributeConsts.NameMaxLength);

            // Attribute adı ÜRÜN başına tekil (aynı üründe iki "Renk" olamaz).
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<ProductAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(ProductAttributeConsts.ValueMaxLength);

            // Değer ATTRIBUTE başına tekil (Renk altında iki "Kırmızı" olamaz).
            b.HasIndex(x => new { x.TenantId, x.ProductAttributeId, x.Value }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<ProductVariantAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductVariantAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Varyant başına attribute başına TEK değer (kombinasyon değişmezi; sınıf yorumuna bakınız).
            b.HasIndex(x => new { x.TenantId, x.ProductVariantId, x.ProductAttributeId }).IsUnique();
            // Değer-bazlı temizlik sorguları (değer silinince varyant senkronu).
            b.HasIndex(x => new { x.TenantId, x.ProductAttributeValueId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<ProductVariantRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductVariantRecipeLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Quantity).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Amount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Factor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.ManualAmount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Description).HasMaxLength(ProductRecipeConsts.DescriptionMaxLength);

            // Varyant reçetesi sıralı okuma (drill LineOrder sırası) + company güvenlik query-filter'ı.
            b.HasIndex(x => new { x.TenantId, x.ProductVariantId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
