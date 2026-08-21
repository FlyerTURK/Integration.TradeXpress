using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ProductCategories;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// core nitelik DEĞERİ ↔ kanal değeri eşleştirmesi mapping'i. Kanal değerine sert FK YOKTUR (nitelik ve
/// kategori eşleştirmeleriyle aynı gerekçe: hedef taksonomi host-global ve yeniden senkronlanabilir).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductCategoryChannelAttributeValueMappings(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductCategoryChannelAttributeValueMapping>(b =>
        {
            b.ToTable(
                TradeXpressConsts.DbTablePrefix + "ProductCategoryChannelAttributeValueMappings",
                TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChannelAttributeValueExternalId)
                .IsRequired()
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength);
            b.Property(x => x.ChannelAttributeValueName)
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelAttributeNameMaxLength);

            // Bir değer bir kategoride bir KANALDA tek karşılığa eşlenir. Soft-delete farkındalı.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId, x.Channel, x.ProductCategoryAttributeValueId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ProductCategoryChannelAttributeValueMapping_Unique");

            // Okuma yönü: "bu kategorinin bu kanaldaki tüm değer eşleştirmeleri" (form + push).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId, x.Channel });
        });
    }
}
