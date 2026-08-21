using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.N11Cities;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// N11 adres taksonomisi (İl/İlçe) mapping'i — <b>host-global</b> (IMultiTenant değil). CityCode/DistrictId global
/// benzersiz (IsDeleted filtreli). Mahalleler saklanmaz (on-demand). Kaynak: SOAP CityService.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Cities(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<N11City>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11Cities", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.CityCode).IsRequired().HasMaxLength(N11CityConsts.CodeMaxLength);
            b.Property(x => x.CityId).IsRequired().HasMaxLength(N11CityConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(N11CityConsts.NameMaxLength);
            b.HasIndex(x => x.CityCode).IsUnique().HasFilter("[IsDeleted] = 0");
            // core coğrafyaya gevşek id-only kolon (nullable) — eşleme sorgusunu hızlandırır.
            b.HasIndex(x => x.CoreAdministrativeAreaId);
        });

        builder.Entity<N11District>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11Districts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.DistrictId).IsRequired().HasMaxLength(N11CityConsts.CodeMaxLength);
            b.Property(x => x.CityCode).IsRequired().HasMaxLength(N11CityConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(N11CityConsts.NameMaxLength);
            b.HasIndex(x => x.DistrictId).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => x.CityCode);
            // core coğrafyaya gevşek id-only kolon (nullable) — eşleme sorgusunu hızlandırır.
            b.HasIndex(x => x.CoreLocalityId);
        });
    }
}
