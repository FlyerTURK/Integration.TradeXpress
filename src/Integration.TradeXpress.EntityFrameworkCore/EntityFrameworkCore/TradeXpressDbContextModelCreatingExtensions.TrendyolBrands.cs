using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.TrendyolBrands;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Trendyol marka write-through cache (host-global) mapping'i — <b>IMultiTenant DEĞİL</b> (TenantId kolonu yok; tüm
/// tenant'lar paylaşır). <see cref="TrendyolBrand.ExternalId"/> (Trendyol id) global benzersiz — soft-delete filtreli
/// (<c>IsDeleted=0</c>) ki silinen kayıt id'yi işgal etmesin (<c>ConfigureTrendyolCategories</c> ikizi; ağaç yok →
/// parent index'i düşer).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureTrendyolBrands(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<TrendyolBrand>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "TrendyolBrands", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // ExternalId long (API id'si int döner ama evren büyüyor → long güvenli); max-length string'e özgüydü, düştü.
            b.Property(x => x.Name).IsRequired().HasMaxLength(TrendyolBrandConsts.NameMaxLength);

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
        });
    }
}
