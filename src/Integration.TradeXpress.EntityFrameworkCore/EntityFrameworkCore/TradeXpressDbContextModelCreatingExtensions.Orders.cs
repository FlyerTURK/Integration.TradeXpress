using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        // Detay snapshot TEK JSON kolonu (value-converter) — iç içe VO tipleri ENTITY olarak KEŞFEDİLMESİN. EF Core
        // runtime konvansiyonu (design-time'da değil) tekil-referans VO'ları (OrderDetailAddress/Party/Totals) ilişkili
        // entity sanıp "requires a primary key" fırlatıyor (SideCostItem'da yok: skaler-only + koleksiyon). Açık Ignore
        // ile modelden düşürülür; converter JSON'a serileştirir. Kök tip (OrderDetailSnapshot) property converter'ıyla
        // skalerdir, ayrıca ignore GEREKMEZ (ve HasConversion ile çakışmasın diye Ignore EDİLMEZ).
        builder.Ignore<OrderDetailSnapshot>();
        builder.Ignore<OrderDetailParty>();
        builder.Ignore<OrderDetailAddress>();
        builder.Ignore<OrderDetailTotals>();
        builder.Ignore<OrderDetailItem>();
        builder.Ignore<OrderDetailItemAttribute>();
        builder.Ignore<OrderLineCustomTextCorrection>();
        builder.Ignore<OrderOperationalParty>();
        builder.Ignore<OrderOperationalAddress>();

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

            // ZENGİN detay (getOrderDetail) — TEK JSON kolonu (value-converter; OrderDetailSnapshotJson, gerekçe orada).
            // SideCosts deseniyle AYNI: derin iç içe owned yapı nvarchar(max)'ta; SetDetail bütün-nesne değişimi yapar
            // (iç mutasyon yok → comparer serileştirilmiş metin üstünden).
            b.Property(x => x.Detail)
                .HasColumnName("Detail")
                .HasConversion(
                    v => OrderDetailSnapshotJson.Serialize(v),
                    v => OrderDetailSnapshotJson.Deserialize(v),
                    new ValueComparer<OrderDetailSnapshot?>(
                        (l, r) => OrderDetailSnapshotJson.Serialize(l) == OrderDetailSnapshotJson.Serialize(r),
                        v => v == null ? 0 : (OrderDetailSnapshotJson.Serialize(v) ?? string.Empty).GetHashCode(),
                        v => OrderDetailSnapshotJson.Deserialize(OrderDetailSnapshotJson.Serialize(v))));

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

        // YEREL/OPERASYONEL katman — OrderLine/Order.Detail'in TERSİNE resync'te SİLİNMEZ/DEĞİŞTİRİLMEZ (bkz. entity
        // XML doc). (OrderId, RemoteLineId) eşleşme anahtarı — OrderLine.Id DEĞİL (satır her çekimde yeniden yaratılır).
        builder.Entity<OrderLineOperationalData>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "OrderLineOperationalData", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.RemoteLineId).IsRequired().HasMaxLength(OrderConsts.RemoteLineIdMaxLength);
            b.Property(x => x.ProductSnapshotName).HasMaxLength(OrderConsts.ProductNameSnapshotMaxLength);
            // Sınırsız (uzun marketplace görsel URL'i) — string default zaten SqlServer'da nvarchar(max). Açık
            // HasColumnType("nvarchar(max)") SQLite test provider'ını ("near max: syntax error") kırdığından kaldırıldı;
            // SqlServer davranışı DEĞİŞMEZ (convention ile yine nvarchar(max)).
            b.Property(x => x.ProductSnapshotImageUrl);
            b.Property(x => x.RejectReason).HasMaxLength(OrderConsts.RejectReasonMaxLength);

            // CustomTextCorrections — küçük liste (Option başına 1), OrderDetailSnapshotJson deseniyle AYNI JSON kolonu.
            b.Property(x => x.CustomTextCorrections)
                .HasColumnName("CustomTextCorrections")
                .HasConversion(
                    v => OrderLineCustomTextCorrectionJson.Serialize(v),
                    v => OrderLineCustomTextCorrectionJson.Deserialize(v),
                    new ValueComparer<List<OrderLineCustomTextCorrection>>(
                        (l, r) => OrderLineCustomTextCorrectionJson.Serialize(l) == OrderLineCustomTextCorrectionJson.Serialize(r),
                        v => OrderLineCustomTextCorrectionJson.Serialize(v).GetHashCode(),
                        v => OrderLineCustomTextCorrectionJson.Deserialize(OrderLineCustomTextCorrectionJson.Serialize(v))));

            // Eşleşme anahtarı: aynı sipariş+kanal-satırı için TEK operasyonel kayıt (insert-only-if-missing garantisi).
            b.HasIndex(x => new { x.TenantId, x.OrderId, x.RemoteLineId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Sipariş BAŞINA TEK operasyonel kayıt (Buyer/Adres/Kargo düzeltmesi) — OrderLineOperationalData ile AYNI
        // resync-bağımsızlık ilkesi, ama satır değil SİPARİŞ düzeyinde.
        builder.Entity<OrderOperationalData>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "OrderOperationalData", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CargoProviderOverride).HasMaxLength(OrderConsts.CargoProviderMaxLength);
            b.Property(x => x.CargoTrackingNumberOverride).HasMaxLength(OrderConsts.CargoTrackingNumberMaxLength);

            b.Property(x => x.BuyerCorrection)
                .HasColumnName("BuyerCorrection")
                .HasConversion(
                    v => OrderOperationalDataJson.SerializeParty(v),
                    v => OrderOperationalDataJson.DeserializeParty(v),
                    new ValueComparer<OrderOperationalParty?>(
                        (l, r) => OrderOperationalDataJson.SerializeParty(l) == OrderOperationalDataJson.SerializeParty(r),
                        v => v == null ? 0 : (OrderOperationalDataJson.SerializeParty(v) ?? string.Empty).GetHashCode(),
                        v => OrderOperationalDataJson.DeserializeParty(OrderOperationalDataJson.SerializeParty(v))));

            b.Property(x => x.BillingAddressCorrection)
                .HasColumnName("BillingAddressCorrection")
                .HasConversion(
                    v => OrderOperationalDataJson.SerializeAddress(v),
                    v => OrderOperationalDataJson.DeserializeAddress(v),
                    new ValueComparer<OrderOperationalAddress?>(
                        (l, r) => OrderOperationalDataJson.SerializeAddress(l) == OrderOperationalDataJson.SerializeAddress(r),
                        v => v == null ? 0 : (OrderOperationalDataJson.SerializeAddress(v) ?? string.Empty).GetHashCode(),
                        v => OrderOperationalDataJson.DeserializeAddress(OrderOperationalDataJson.SerializeAddress(v))));

            b.Property(x => x.ShippingAddressCorrection)
                .HasColumnName("ShippingAddressCorrection")
                .HasConversion(
                    v => OrderOperationalDataJson.SerializeAddress(v),
                    v => OrderOperationalDataJson.DeserializeAddress(v),
                    new ValueComparer<OrderOperationalAddress?>(
                        (l, r) => OrderOperationalDataJson.SerializeAddress(l) == OrderOperationalDataJson.SerializeAddress(r),
                        v => v == null ? 0 : (OrderOperationalDataJson.SerializeAddress(v) ?? string.Empty).GetHashCode(),
                        v => OrderOperationalDataJson.DeserializeAddress(OrderOperationalDataJson.SerializeAddress(v))));

            // Sipariş başına TEK kayıt.
            b.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
