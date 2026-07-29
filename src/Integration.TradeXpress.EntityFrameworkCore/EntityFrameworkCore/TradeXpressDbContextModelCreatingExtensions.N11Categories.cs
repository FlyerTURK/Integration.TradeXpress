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

            // Komisyon (yaprakta dolu) — matematik yapılır → decimal, oran için (9,4) yeter (ör. 20.3400, 0.6700).
            b.Property(x => x.CommissionRate).HasPrecision(9, 4);
            b.Property(x => x.MarketingFeeRate).HasPrecision(9, 4);
            b.Property(x => x.MarketplaceFeeRate).HasPrecision(9, 4);

            // Senkron damgası — konvansiyonla (nullable datetime2). İNDEKS YOK: bayatlık kapısı tabloda tek bir
            // MAX(LastSyncedAt) okur, birkaç bin satırda maliyeti ihmal edilebilir.

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => x.ParentExternalId);
        });
    }
}
