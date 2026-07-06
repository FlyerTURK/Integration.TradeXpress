using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.N11Categories;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// N11 kategori (host-global taksonomi) mapping'i — <b>IMultiTenant DEĞİL</b> (TenantId kolonu yok; tüm tenant'lar
/// paylaşır). <see cref="N11Category.ExternalId"/> (N11 id) global benzersiz — soft-delete filtreli
/// (<c>IsDeleted=0</c>) ki silinen düğüm id'yi işgal etmesin. ParentExternalId index'i ağaç sorgusu içindir.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Categories(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<N11Category>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11Categories", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(N11CategoryConsts.ExternalIdMaxLength);
            b.Property(x => x.ParentExternalId).HasMaxLength(N11CategoryConsts.ExternalIdMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(N11CategoryConsts.NameMaxLength);

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => x.ParentExternalId);
        });
    }
}
