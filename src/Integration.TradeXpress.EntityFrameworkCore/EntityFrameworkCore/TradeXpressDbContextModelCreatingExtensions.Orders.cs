using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Orders;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Sipariş (Order) mapping'i — NÖTR sipariş + satırları (company-owned, per-tenant). Salt-okuma çekim (O0):
/// pazaryerinden GET + idempotent upsert. Satırlar KENDİ tablolarında (owned JSON değil) — id-only OrderId bağı,
/// persistence açıkça yönetilir (çekimde sil+yaz).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureOrders(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Order>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Orders", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.RemoteOrderId).IsRequired().HasMaxLength(OrderConsts.RemoteOrderIdMaxLength);
            b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(OrderConsts.OrderNumberMaxLength);
            b.Property(x => x.RemoteStatus).HasMaxLength(OrderConsts.RemoteStatusMaxLength);
            b.Property(x => x.CustomerName).HasMaxLength(OrderConsts.CustomerNameMaxLength);
            b.Property(x => x.CargoProvider).HasMaxLength(OrderConsts.CargoProviderMaxLength);
            b.Property(x => x.CargoTrackingNumber).HasMaxLength(OrderConsts.CargoTrackingNumberMaxLength);
            b.Property(x => x.TotalAmount).HasPrecision(18, 2);

            // İdempotency BEL KEMİĞİ: (SalesChannelId, RemoteOrderId) tekil — ikinci çekim aynı siparişi bulup GÜNCELLER,
            // dublike üretmez. TenantId de anahtarda (kanal per-tenant zaten kapsar; simetri + host/tenant izolasyonu).
            // IsDeleted=0 filtresi: soft-delete edilmiş satır kodu işgal etmesin (SalesChannel/Product deseni).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelId, x.RemoteOrderId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
            // Company güvenlik query-filter'ını + kanal/tarih listelemesini hızlandırır.
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            b.HasIndex(x => new { x.TenantId, x.SalesChannelId, x.OrderDate });
        });

        builder.Entity<OrderLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "OrderLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.RemoteLineId).HasMaxLength(OrderConsts.RemoteLineIdMaxLength);
            b.Property(x => x.Barcode).HasMaxLength(OrderConsts.BarcodeMaxLength);
            b.Property(x => x.StockCode).HasMaxLength(OrderConsts.StockCodeMaxLength);
            b.Property(x => x.ProductNameSnapshot).IsRequired().HasMaxLength(OrderConsts.ProductNameSnapshotMaxLength);
            b.Property(x => x.RemoteLineStatus).HasMaxLength(OrderConsts.RemoteLineStatusMaxLength);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.Property(x => x.LineTotal).HasPrecision(18, 2);

            // Sipariş başına satırları sil+yaz için; company güvenlik filtresi; O1 varyant bağı taraması.
            b.HasIndex(x => new { x.TenantId, x.OrderId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            b.HasIndex(x => new { x.TenantId, x.ProductVariantId });
        });
    }
}
