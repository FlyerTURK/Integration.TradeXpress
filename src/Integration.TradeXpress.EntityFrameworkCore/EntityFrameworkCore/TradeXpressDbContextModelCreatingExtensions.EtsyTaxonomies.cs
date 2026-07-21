using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.EtsyTaxonomies;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Etsy seller taxonomy (host-global taksonomi) mapping'i — <b>IMultiTenant DEĞİL</b> (TenantId kolonu yok; tüm
/// tenant'lar paylaşır). <see cref="EtsyTaxonomy.ExternalId"/> (Etsy node id) global benzersiz — soft-delete
/// filtreli (<c>IsDeleted=0</c>) ki silinen düğüm id'yi işgal etmesin. ParentExternalId index'i ağaç sorgusu içindir.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureEtsyTaxonomies(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<EtsyTaxonomy>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EtsyTaxonomies", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(EtsyTaxonomyConsts.ExternalIdMaxLength);
            b.Property(x => x.ParentExternalId).HasMaxLength(EtsyTaxonomyConsts.ExternalIdMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EtsyTaxonomyConsts.NameMaxLength);

            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => x.ParentExternalId);
        });
    }
}
