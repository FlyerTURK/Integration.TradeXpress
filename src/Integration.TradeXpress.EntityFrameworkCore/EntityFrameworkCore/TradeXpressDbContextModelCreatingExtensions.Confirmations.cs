using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Confirmations;
using Integration.TradeXpress.Vouchers;   // VoucherConsts (Amount hassasiyeti)

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Teyit (organizasyon-içi karşılıklı mirror onayı) mapping'i. Company-owned; başlatan/karşı birer
/// <see cref="Vaults.Vault"/> (kasa). Initiator/CounterpartyVoucherId id-only mantıksal referans (FK YOK —
/// BalanceLedger deseniyle hizalı; postlama app-katmanında materyalize edilir).
/// <para>İki payload (Initiator/Counterparty) opak process satırıdır — her taraf KENDİ satırını yazar.
/// Skaler alanlar (emtia/varyant/miktar/tutar/birimler) payload'un denormalize MIRROR ANAHTARI'dır (ConfirmationMirrorKey).
/// Emtia/varyant FK'sı YOKTUR: emtia tipe göre farklı tabloda yaşar (Cash/Metal/Stone/Good…) → tek FK
/// hedefi kurulamaz; BalanceLedger'ın CommodityId deseniyle hizalı id-only referans.</para>
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureConfirmations(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Confirmation>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Confirmations", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Mirror kriteri tutarları — N2 (VoucherLine ile aynı hassasiyet).
            b.Property(x => x.Amount).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Quantity).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.PayTotal).HasPrecision(VoucherConsts.AmountPrecision, VoucherConsts.AmountScale);
            b.Property(x => x.Note).HasMaxLength(ConfirmationConsts.NoteMaxLength);
            b.Property(x => x.DecisionNote).HasMaxLength(ConfirmationConsts.DecisionNoteMaxLength);
            b.Property(x => x.InitiatorPayloadJson).IsRequired().HasMaxLength(ConfirmationConsts.PayloadMaxLength);
            b.Property(x => x.CounterpartyPayloadJson).HasMaxLength(ConfirmationConsts.PayloadMaxLength);

            // Gelen/giden kutusu: şirket + başlatan/karşı kasa + durum.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.InitiatorVaultId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.CounterpartyVaultId, x.Status });

            // FK'lar — id-only (nav YOK); referans varken silme engeli (Restrict). Başlatan+karşı kasa ZORUNLU;
            // ana/karşılık birimi OPSİYONEL: her tipte ikisi birden dolu olmaz (Dekont'ta MainUnitId boş,
            // değerlemesiz teslimde PayUnitId boş).
            b.HasOne<Companies.Company>().WithMany()
                .HasForeignKey(x => x.CompanyId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Vaults.Vault>().WithMany()
                .HasForeignKey(x => x.InitiatorVaultId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Vaults.Vault>().WithMany()
                .HasForeignKey(x => x.CounterpartyVaultId).IsRequired().OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany()
                .HasForeignKey(x => x.MainUnitId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Integration.TradeXpress.Financials.CurrencyUnits.CurrencyUnit>().WithMany()
                .HasForeignKey(x => x.PayUnitId).OnDelete(DeleteBehavior.Restrict);

            // Initiator/CounterpartyVoucherId: id-only mantıksal referans (FK YOK; postlama app-katmanında).
        });
    }
}
