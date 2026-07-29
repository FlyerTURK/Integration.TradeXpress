using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ProductCategories;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Çekirdek kategori ↔ kanal kategorisi eşleştirmesi mapping'i. Kanal kategorisine SERT FK YOKTUR: hedef
/// taksonomiler host-global tablolarda yaşar (<c>AppN11Categories</c> vb.) ve yeniden senkronlandıklarında
/// satırları değişebilir — sert FK, kanal senkronunu eşleştirmelerimiz yüzünden kilitlerdi.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductCategoryChannelMappings(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductCategoryChannelMapping>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductCategoryChannelMappings", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ChannelCategoryExternalId)
                .IsRequired()
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelCategoryIdMaxLength);
            b.Property(x => x.ChannelCategoryName)
                .HasMaxLength(ProductCategoryChannelMappingConsts.ChannelCategoryNameMaxLength);

            // Bir kategori bir KANALDA tek karşılığa eşlenir — çift eşleştirme "hangisi geçerli" belirsizliği
            // yaratır ve komisyon çözümünü rastgele kılardı. Soft-delete farkındalı (silinen eşleştirme yeniden kurulabilsin).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId, x.Channel })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // Çözüm yönü: "bu kategori(ler) için kanal X eşleştirmesi var mı" — ata zinciri yukarı yürünürken kullanılır.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Channel, x.ProductCategoryId });
        });
    }
}
