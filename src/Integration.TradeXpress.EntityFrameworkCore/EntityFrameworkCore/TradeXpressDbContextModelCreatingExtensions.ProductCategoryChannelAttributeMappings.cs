using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ProductCategories;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Çekirdek NİTELİK ↔ kanal niteliği eşleştirmesi mapping'i. Kanal niteliğine sert FK YOKTUR (kategori
/// eşleştirmesiyle aynı gerekçe: hedef taksonomi host-global ve yeniden senkronlanabilir).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductCategoryChannelAttributeMappings(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductCategoryChannelAttributeMapping>(b =>
        {
            b.ToTable(
                TradeXpressConsts.DbTablePrefix + "ProductCategoryChannelAttributeMappings",
                TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChannelAttributeExternalId)
                .IsRequired()
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelAttributeIdMaxLength);
            b.Property(x => x.ChannelAttributeName)
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelAttributeNameMaxLength);

            // Bir nitelik bir kategoride bir KANALDA tek karşılığa eşlenir — çift satır, push'ta hangi kanal
            // niteliğine yazılacağını belirsiz bırakırdı. Soft-delete farkındalı.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId, x.Channel, x.ProductCategoryAttributeId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_ProductCategoryChannelAttributeMapping_Unique");

            // Okuma yönü: "bu kategorinin bu kanaldaki tüm nitelik eşleştirmeleri" (form + push).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId, x.Channel });
        });
    }
}
