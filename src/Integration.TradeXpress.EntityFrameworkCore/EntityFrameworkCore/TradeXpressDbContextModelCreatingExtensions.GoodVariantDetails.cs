using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Goods;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Good varyant fiyat/stok UZANTISI mapping'i — jenerik EntityVariant'ın Good facet'i (1:1, EntityVariantId).
/// Perakende fiyat/stok varyant seviyesinde. Owned Margin VO (Type + Value). id-only bağ (sert FK yok).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureGoodVariantDetails(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<GoodVariantDetail>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "GoodVariantDetails", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.StockUnitCode).HasMaxLength(GoodConsts.StockUnitMaxLength);
            b.Property(x => x.EntryPrice).HasPrecision(GoodConsts.PricePrecision, GoodConsts.PriceScale);
            b.Property(x => x.ExitPrice).HasPrecision(GoodConsts.PricePrecision, GoodConsts.PriceScale);
            b.Property(x => x.MinQuantity).HasPrecision(GoodConsts.QuantityPrecision, GoodConsts.QuantityScale);
            b.Property(x => x.MaxQuantity).HasPrecision(GoodConsts.QuantityPrecision, GoodConsts.QuantityScale);

            // Kâr şekli — owned VO (Type enum EXPLICIT map; convention get-only enum'u atlar). Value PricePrecision.
            b.OwnsOne(x => x.Margin, m =>
            {
                m.Property(p => p.Type);
                m.Property(p => p.Value).HasPrecision(GoodConsts.PricePrecision, GoodConsts.PriceScale);
            });
            b.Navigation(x => x.Margin).IsRequired();

            // 1:1 — jenerik varyant başına tek Good detayı.
            b.HasIndex(x => new { x.TenantId, x.EntityVariantId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
