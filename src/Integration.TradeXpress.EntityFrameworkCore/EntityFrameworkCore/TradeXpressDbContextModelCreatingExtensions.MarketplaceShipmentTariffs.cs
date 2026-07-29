using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.MarketplaceShipmentTariffs;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Pazaryeri anlaşmalı kargo tarifesi mapping'i — <b>HOST-GLOBAL</b> (N11City deseni): tenant kolonu YOK,
/// <c>HasQueryFilter</c> YAZILMAZ, benzersizlik global.
/// <para>Kimlik: (Channel, CarrierCode, EffectiveFrom) — aynı kanal+taşıyıcı için aynı yürürlük gününde iki
/// tarife olamaz; yeni yayın YENİ bir <c>EffectiveFrom</c> ile girer, eskisi kapatılır ama SİLİNMEZ.</para>
/// <para>Desi satırları AYRI tablo (indeksten okunur), şartlı barem owned JSON (kanal başına 2 satır).</para>
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureMarketplaceShipmentTariffs(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<MarketplaceShipmentTariff>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "MarketplaceShipmentTariffs", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.CarrierCode).IsRequired()
                .HasMaxLength(MarketplaceShipmentTariffConsts.CarrierCodeMaxLength);
            b.Property(x => x.CarrierName).IsRequired()
                .HasMaxLength(MarketplaceShipmentTariffConsts.CarrierNameMaxLength);
            b.Property(x => x.ChannelCompanyExternalId)
                .HasMaxLength(MarketplaceShipmentTariffConsts.ChannelCompanyExternalIdMaxLength);
            b.Property(x => x.SourceVersion).IsRequired()
                .HasMaxLength(MarketplaceShipmentTariffConsts.SourceVersionMaxLength);

            b.Property(x => x.OverflowIncrementAmount).HasPrecision(
                MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
            b.Property(x => x.ExtraFeeAmount).HasPrecision(
                MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
            b.Property(x => x.HeavyCargoAmount).HasPrecision(
                MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);

            b.Property(x => x.VatRate).HasPrecision(
                MarketplaceShipmentTariffConsts.RatePrecision, MarketplaceShipmentTariffConsts.RateScale);
            b.Property(x => x.PostalServiceFeeRate).HasPrecision(
                MarketplaceShipmentTariffConsts.RatePrecision, MarketplaceShipmentTariffConsts.RateScale);
            b.Property(x => x.FailedDeliveryRate).HasPrecision(
                MarketplaceShipmentTariffConsts.RatePrecision, MarketplaceShipmentTariffConsts.RateScale);

            b.Property(x => x.IsActive).HasDefaultValue(true);

            // Yürürlük penceresi tarih bazlıdır (saat taşımaz) — gün kayması yasak (CLAUDE.md zaman kuralı).
            b.Property(x => x.EffectiveFrom).HasColumnType("date");
            b.Property(x => x.EffectiveTo).HasColumnType("date");

            // Şartlı barem: sorgulanmayan, kanal başına 2 satırlık liste → owned JSON.
            b.OwnsMany(x => x.ConditionalRates, r =>
            {
                r.ToJson("ConditionalRates");
                r.Property(p => p.BasketFrom).HasPrecision(
                    MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
                r.Property(p => p.BasketTo).HasPrecision(
                    MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
                r.Property(p => p.Amount).HasPrecision(
                    MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
            });

            // Desi satırları: owned ama AYRI TABLO (JSON değil) — "şu desi kaç TL" sorgusu indeksten
            // karşılanmalı. Doğal anahtar (TariffId, Desi): satırın kendi kimliği yok, tarifesiyle yaşar.
            b.OwnsMany(x => x.Rates, r =>
            {
                r.ToTable(TradeXpressConsts.DbTablePrefix + "MarketplaceShipmentTariffRates", TradeXpressConsts.DbSchema);
                r.WithOwner().HasForeignKey(x => x.TariffId);
                r.HasKey(x => new { x.TariffId, x.Desi });
                // Desi ANLAMLI bir değer (kaçıncı desi), üretilen bir kimlik değil. Belirtilmezse EF int PK'yı
                // IDENTITY sayar ve SQL Server "IDENTITY özelliği değiştirilemez" diye migration'ı reddeder.
                r.Property(x => x.Desi).ValueGeneratedNever();
                r.Property(x => x.Amount).HasPrecision(
                    MarketplaceShipmentTariffConsts.AmountPrecision, MarketplaceShipmentTariffConsts.AmountScale);
            });

            // TENANT KOLU YOK — host-global. Aynı kanal+taşıyıcı+yürürlük günü tek satır.
            b.HasIndex(x => new { x.Channel, x.CarrierCode, x.EffectiveFrom })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            // "Şu an yürürlükte olan tarifeler" sorgusunun tarama alanı.
            b.HasIndex(x => new { x.Channel, x.EffectiveTo });
        });

    }
}
