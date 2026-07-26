using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.Framework.Addressing;
using Integration.TradeXpress.N11Shipments;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>N11 kargo firması (host-global) + kargo şablonu (per-kanal) mapping'i. Şablon adresleri yeniden-kullanılabilir
/// <see cref="Address"/> VO (OwnsOne); firma/il id-listeleri primitive collection; kimlik (SalesChannelId, TemplateName).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Shipments(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<N11ShipmentCompany>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11ShipmentCompanies", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ExternalId).IsRequired().HasMaxLength(N11ShipmentConsts.ExternalIdMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(N11ShipmentConsts.NameMaxLength);
            // ShortName OPSİYONEL — N11 kısa-kodsuz firma döndürebiliyor (bkz. entity notu); zorunluluk sync'i düşürüyordu.
            b.Property(x => x.ShortName).HasMaxLength(N11ShipmentConsts.ShortNameMaxLength);
            b.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsDeleted] = 0");
            // CoreCarrierId + indeksi KALDIRILDI (2026-07-26): host-global ayna company-owned satırı
            // adresleyemez. Şirkete ait bağ (firma → varsayılan cari) şablonun içinde yaşıyor.
        });

        builder.Entity<N11ShipmentTemplate>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "N11ShipmentTemplates", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.TemplateName).IsRequired().HasMaxLength(N11ShipmentConsts.TemplateNameMaxLength);
            // Mevcut şablonlar AKTİF doğmalı — CLR default'u false olduğundan DB default'u açıkça true verilir,
            // yoksa kolon eklenirken tüm şablonlar pasifleşir (senkron onları N11'de bulup geri açana kadar görünmez).
            b.Property(x => x.IsActive).HasDefaultValue(true);
            b.Property(x => x.ShippingInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
            b.Property(x => x.ExchangeInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
            b.Property(x => x.InstallmentInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
            b.Property(x => x.CargoAccountNo).HasMaxLength(N11ShipmentConsts.CargoAccountNoMaxLength);
            b.Property(x => x.ClaimShipmentCompanyExternalId).HasMaxLength(N11ShipmentConsts.ExternalIdMaxLength);

            // Şartlı kargo eşiği (N11 "Şartlı Kargo") — şablon-düzeyinde skaler. API-read-only (import ile dolar).
            b.Property(x => x.ConditionalShippingThreshold).HasColumnType("decimal(18,2)");
            // Enum 1'den başlar (CLR default 0 geçersiz) → sentinel'i Amount'a sabitle: DB default'u yalnız gerçekten
            // "ayarlanmamış" halde kullansın (aksi halde EF "sentinel yok" uyarısı verir; değer zaten hep 1/2 yazılır).
            b.Property(x => x.ConditionalShippingUnit)
                .HasDefaultValue(N11ConditionalShippingUnit.Amount)
                .HasSentinel(N11ConditionalShippingUnit.Amount);

            // Adresler = yeniden-kullanılabilir Address VO (OwnsOne; aynı tabloda prefix'li kolonlar). Depo zorunlu,
            // değişim opsiyonel — City/Line required (owned) → EF null-tespiti (tüm kolonlar null → değişim adresi null).
            b.OwnsOne(x => x.WarehouseAddress, ConfigureAddress);
            b.OwnsOne(x => x.ExchangeAddress, ConfigureAddress);

            // Kargo firmaları artık düz kimlik listesi DEĞİL: her satır firma + varsayılan cari alt hesap taşıyor.
            // JSON kolonu (owned collection; sorgulanmaz, N11'e yalnız kimlik push edilir) — Etsy attribute deseni.
            b.OwnsMany(x => x.Companies, c =>
            {
                c.ToJson("ShipmentCompanies");   // kolon adı SABİT — property rename şema değiştirmez
                c.Property(p => p.ExternalId).HasMaxLength(N11ShipmentConsts.ExternalIdMaxLength);
            });

            // İl kodları düz kimlik listesi olarak kalır (JSON kolonu; JOIN gerekmez, N11'e push edilir).
            b.PrimitiveCollection(x => x.DeliverableCityCodes);

            // Kimlik = (SalesChannelId, TemplateName) — N11'de ayrı id yok; soft-delete filtreli.
            b.HasIndex(x => new { x.SalesChannelId, x.TemplateName }).IsUnique().HasFilter("[IsDeleted] = 0");
        });
    }

    /// <summary>Gömülü <see cref="Address"/> VO kolon yapılandırması (depo + değişim adresi ortak). City/Line required
    /// → opsiyonel değişim adresinde EF'in null-tespiti için (tüm owned kolonlar null ⇒ navigation null).</summary>
    private static void ConfigureAddress<TOwner>(OwnedNavigationBuilder<TOwner, Address> a)
        where TOwner : class
    {
        a.Property(p => p.Title).HasMaxLength(AddressConsts.TitleMaxLength);
        a.Property(p => p.CountryCode).HasMaxLength(AddressConsts.CountryCodeMaxLength);
        a.Property(p => p.City).IsRequired().HasMaxLength(AddressConsts.CityMaxLength);
        a.Property(p => p.District).HasMaxLength(AddressConsts.DistrictMaxLength);
        a.Property(p => p.Neighborhood).HasMaxLength(AddressConsts.NeighborhoodMaxLength);
        a.Property(p => p.Line).IsRequired().HasMaxLength(AddressConsts.LineMaxLength);
        a.Property(p => p.PostalCode).HasMaxLength(AddressConsts.PostalCodeMaxLength);
        a.Property(p => p.CityCode).HasMaxLength(AddressConsts.CodeMaxLength);
        a.Property(p => p.DistrictCode).HasMaxLength(AddressConsts.CodeMaxLength);

        // Opsiyonel coğrafya referansları (additive) — id-only köprü + ISO 3166-2 kodu; hepsi NULLABLE (IsRequired YOK).
        // Nullable CLR tipleri konvansiyonla nullable kolona map olur; ISO kodu için uzunluk açıkça sabitlenir.
        a.Property(p => p.AdministrativeAreaId);
        a.Property(p => p.LocalityId);
        a.Property(p => p.AdministrativeAreaIsoCode).HasMaxLength(AddressConsts.IsoSubentityCodeMaxLength);

        // UBL PostalAddress zenginleştirme kolonları (opsiyonel; hepsi NULLABLE) — bina/oda/kat/posta-kutusu + ek cadde.
        a.Property(p => p.BuildingName).HasMaxLength(AddressConsts.BuildingNameMaxLength);
        a.Property(p => p.BuildingNumber).HasMaxLength(AddressConsts.BuildingNumberMaxLength);
        a.Property(p => p.Room).HasMaxLength(AddressConsts.RoomMaxLength);
        a.Property(p => p.Floor).HasMaxLength(AddressConsts.FloorMaxLength);
        a.Property(p => p.Postbox).HasMaxLength(AddressConsts.PostboxMaxLength);
        a.Property(p => p.AdditionalStreetName).HasMaxLength(AddressConsts.AdditionalStreetNameMaxLength);
    }
}
