using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.TrendyolCategories;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Trendyol kategori (host-global taksonomi) mapping'i — <b>IMultiTenant DEĞİL</b> (TenantId kolonu yok; tüm tenant'lar
/// paylaşır). <see cref="TrendyolCategory.ExternalId"/> (Trendyol id) global benzersiz — soft-delete filtreli
/// (<c>IsDeleted=0</c>) ki silinen düğüm id'yi işgal etmesin. ParentExternalId index'i ağaç sorgusu içindir
/// (<c>ConfigureN11Categories</c> ikizi; Trendyol'da komisyon alanları yok).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureTrendyolCategories(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<TrendyolCategory>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "TrendyolCategories", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(TrendyolCategoryConsts.ExternalIdMaxLength);
            b.Property(x => x.ParentExternalId).HasMaxLength(TrendyolCategoryConsts.ExternalIdMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(TrendyolCategoryConsts.NameMaxLength);

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => x.ParentExternalId);
        });
    }
}
