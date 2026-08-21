using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Geography;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// core coğrafya (idari alan/yerellik/alt-yerellik — ISO 3166-2 hizalı) mapping'i. <b>HOST-GLOBAL</b>
/// (IMultiTenant DEĞİL → TenantId kolonu yok; N11City deseni). İdari alan ISO kodu ülke-içinde benzersiz —
/// YALNIZ doldurulduğunda (nullable kolonda tek-NULL tuzağına düşmemek için <c>IS NOT NULL</c> filtresi).
/// Kaynak: N11 türetme + ISO katalog.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureGeography(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<AdministrativeArea>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "AdministrativeAreas", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(GeographyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(GeographyConsts.NameMaxLength);
            b.Property(x => x.Iso3166_2Code).HasMaxLength(GeographyConsts.Iso3166_2CodeMaxLength);
            b.Property(x => x.Category).HasMaxLength(GeographyConsts.CategoryMaxLength);
            // Per-state yerellik importu işareti (null = bu eyaletin şehirleri henüz çekilmedi) — iki-seviyeli lazy.
            b.Property(x => x.LocalitiesImportedAt);

            // ISO 3166-2 alt-bölüm kodu ülke-içinde benzersiz — YALNIZ dolu iken (IS NOT NULL): nullable kolonda
            // SQL Server "tek-NULL" kısıtı ISO'suz alanları bloklamasın. Soft-delete edilmiş satır da hariç.
            b.HasIndex(x => new { x.CountryId, x.Iso3166_2Code })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0 AND [Iso3166_2Code] IS NOT NULL");
            b.HasIndex(x => x.CountryId);
        });

        builder.Entity<Locality>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Localities", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(GeographyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(GeographyConsts.NameMaxLength);

            b.HasIndex(x => x.AdministrativeAreaId);
            b.HasIndex(x => x.CountryId);
        });

        builder.Entity<SubLocality>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SubLocalities", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(GeographyConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(GeographyConsts.NameMaxLength);

            b.HasIndex(x => x.LocalityId);
        });
    }
}
