using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.N11Shipments;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Satış kanalı (SalesChannel) mapping'i — <b>TPT (Table-Per-Type)</b> hiyerarşisi + <b>company-owned</b>.
/// Soyut taban <see cref="SalesChannelBase"/> → <c>AppSalesChannels</c> (ortak kimlik + sahiplik alanları);
/// her somut alt-tip kendi tablosunu ekler (paylaşılan PK/FK). Benzersizlik company-scoped
/// <c>(TenantId, CompanyId, Code)</c> — taban tabloda (Product deseni).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureSalesChannels(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // ── Soyut TPT tabanı → AppSalesChannels (ortak alanlar + company güvenlik sınırı) ──
        builder.Entity<SalesChannelBase>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannels", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();
            b.UseTptMappingStrategy();   // base + her alt-tip AYRI tablo (paylaşılan PK/FK)

            b.Property(x => x.Code).IsRequired().HasMaxLength(SalesChannelConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(SalesChannelConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(SalesChannelConsts.DescriptionMaxLength);

            // Kod company-scoped benzersiz (Product deseni: (TenantId, CompanyId, Code)). IsDeleted=0 FİLTRESİ
            // ZORUNLU: soft-delete'te satır tabloda kalır; filtresiz index silinmiş kaydın kodunu işgal ederek
            // yeniden kullanımı engellerdi (app-katmanı benzersizlik kontrolü zaten soft-delete'i filtreler → hizala).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            // Company güvenlik query-filter'ını hızlandırır (ICompanyOwned).
            b.HasIndex(x => new { x.TenantId, x.CompanyId });

            // Varsayılan paket desisi — MEVCUT kanallar 1 ile doğmalı. CLR default'u 0 olduğundan DB default'u
            // AÇIKÇA verilir; yoksa kolon eklenirken tüm kanallar "0 desi" olur ve kargo tarifesi sessizce
            // en alt basamaktan (Dosya) fiyatlar (N11ShipmentTemplate.IsActive'de aynı tuzağa düşülmüştü).
            b.Property(x => x.DefaultPackageDesi).HasDefaultValue(1);

            // Muhasebe cari alt hesabı — id-only (nav YOK, FK YOK: aggregate'ler arası bağ id ile kurulur).
            // İndeks GEREKMEZ: kanal sayısı şirket başına bir avuç, bu alan üzerinden sorgu yapılmıyor.
            b.Property(x => x.SubAccountId);

            // Yan-maliyet (gider) ayarları — kanal-agnostik VO, TEK JSON kolonu (base tabloda; alt tiplere alan
            // yayılmaz). EF native ToJson() TPT'de DESTEKLENMİYOR ("Only TPH inheritance is supported") → değer
            // dönüştürücü (SideCostSettingsJson; gerekçe orada). Değişim tespiti: SetSideCosts bütün-nesne değişimi
            // yapar; comparer serileştirilmiş metin üstünden (iç mutasyon yok — VO immutable kullanılır).
            b.Property(x => x.SideCosts)
                .HasColumnName("SideCosts")
                .HasConversion(
                    v => SideCostSettingsJson.Serialize(v),
                    v => SideCostSettingsJson.Deserialize(v),
                    new ValueComparer<SideCostSettings>(
                        (l, r) => SideCostSettingsJson.Serialize(l) == SideCostSettingsJson.Serialize(r),
                        v => (SideCostSettingsJson.Serialize(v) ?? string.Empty).GetHashCode(),
                        v => SideCostSettingsJson.Deserialize(SideCostSettingsJson.Serialize(v))!));
        });

        // ── Somut alt-tip: N11 (Türkiye pazaryeri) → AppSalesChannelTrN11 (API kimlik bilgileri) ──
        builder.Entity<SalesChannelTrN11>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11", TradeXpressConsts.DbSchema);

            // Opak sir: normalize edilmez, yalnız uzunluk kısıtı. TODO(hardening): AppSecret at-rest şifrelenmeli.
            b.Property(x => x.AppKey).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.AppSecret).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);

            // Kanal düzeyi varsayılan bilgi metinleri (opsiyonel) — yeni N11 kargo şablonu formunu ön-doldurur.
            b.Property(x => x.DefaultShippingInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
            b.Property(x => x.DefaultExchangeInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
            b.Property(x => x.DefaultInstallmentInfo).HasMaxLength(N11ShipmentConsts.InfoMaxLength);
        });

        // ── Somut alt-tip: Trendyol (Türkiye pazaryeri) → AppSalesChannelTrTrendyol (Satıcı ID + API kimlik bilgileri) ──
        builder.Entity<SalesChannelTrTrendyol>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyol", TradeXpressConsts.DbSchema);

            // Opak kimlik/sir: normalize edilmez, yalnız uzunluk kısıtı. TODO(hardening): ApiSecret at-rest şifrelenmeli.
            b.Property(x => x.SellerId).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.ApiKey).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.ApiSecret).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
        });

        // ── Somut alt-tip: Etsy (global platform — Tr öneki YOK) → AppSalesChannelEtsy (OAuth 2.0 PKCE kimliği) ──
        builder.Entity<SalesChannelEtsy>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsy", TradeXpressConsts.DbSchema);

            // Uygulama kimliği (opak; normalize edilmez). Keystring = public client_id (sır değil), SharedSecret = sır.
            // TODO(hardening): SharedSecret + token'lar at-rest şifrelenmeli (Trendyol ApiSecret TODO'suyla hizalı).
            b.Property(x => x.Keystring).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.SharedSecret).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);

            // OAuth bağlantı verisi — TÜMÜ nullable (bağlanmamış kanal geçerli durumdur; "Bağlan" akışı doldurur).
            b.Property(x => x.ShopId).HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.ShopName).HasMaxLength(SalesChannelConsts.NameMaxLength);
            b.Property(x => x.EtsyUserId).HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.AccessToken).HasMaxLength(SalesChannelConsts.OAuthTokenMaxLength);
            b.Property(x => x.RefreshToken).HasMaxLength(SalesChannelConsts.OAuthTokenMaxLength);
        });
    }
}
