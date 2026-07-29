using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Ürün GENEL ÖZELLİK değerleri mapping'i — kategoriden gelen spesifikasyon niteliğinin ürüne yazılan değeri
/// ("Ayar: 22K"). Nitelik bağı id-only (sert FK yok; kategori tarafı ayrı aggregate).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductSpecifications(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductSpecification>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductSpecifications", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(ProductConsts.SpecificationValueMaxLength);

            // Ürün başına bir nitelik TEK değer alır — ikinci satır "hangisi geçerli" belirsizliği doğurur ve
            // pazaryeri push'unda rastgele biri giderdi. Soft-delete farkındalı (silinmiş satır yeri işgal etmesin).
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.ProductCategoryAttributeId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // Ürünün tüm özelliklerini tek okumada getirmek için (form + push).
            b.HasIndex(x => new { x.TenantId, x.ProductId });
        });
    }
}
