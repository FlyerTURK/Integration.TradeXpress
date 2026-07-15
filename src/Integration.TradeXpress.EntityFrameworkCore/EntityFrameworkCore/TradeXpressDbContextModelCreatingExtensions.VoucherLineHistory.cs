using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Fiş satırı değişim günlüğü (gölge — çekirdek posting/bakiyeyi ETKİLEMEZ) mapping'i. VoucherLine ayrı
/// tablo olmadığından (Voucher'ın owned koleksiyonu) VoucherLineId/VoucherId id-only mantıksal referanstır
/// (FK YOK — BalanceLedger/Confirmation deseniyle hizalı).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureVoucherLineHistories(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<VoucherLineHistory>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "VoucherLineHistories", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.VoucherNumber).IsRequired().HasMaxLength(VoucherLineHistoryConsts.CommodityCodeMaxLength);
            b.Property(x => x.ProcessCode).IsRequired().HasMaxLength(VoucherLineHistoryConsts.CommodityCodeMaxLength);
            b.Property(x => x.CommodityCode).HasMaxLength(VoucherLineHistoryConsts.CommodityCodeMaxLength);
            b.Property(x => x.MainUnitCode).HasMaxLength(VoucherLineHistoryConsts.MainUnitCodeMaxLength);
            b.Property(x => x.Description).HasMaxLength(VoucherLineHistoryConsts.DescriptionMaxLength);
            b.Property(x => x.SnapshotJson).IsRequired().HasMaxLength(VoucherLineHistoryConsts.SnapshotMaxLength);

            b.Property(x => x.Quantity).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Total).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);

            // Log tab sorgusu: karşı taraf (SubAccount/Kasa) + tarih aralığı.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.SubAccountId, x.CreationTime });

            // Popup sorgusu: tek satırın tam tarihçesi.
            b.HasIndex(x => x.VoucherLineId);

            // VoucherId/VoucherLineId: id-only mantıksal referans (FK YOK — VoucherLine ayrı tablo değil).
        });
    }
}
