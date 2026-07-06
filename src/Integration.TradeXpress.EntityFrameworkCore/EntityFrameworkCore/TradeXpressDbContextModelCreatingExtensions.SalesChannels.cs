using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.SalesChannels;

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
        });

        // ── Somut alt-tip: N11 (Türkiye pazaryeri) → AppSalesChannelTrN11 (API kimlik bilgileri) ──
        builder.Entity<SalesChannelTrN11>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11", TradeXpressConsts.DbSchema);

            // Opak sir: normalize edilmez, yalnız uzunluk kısıtı. TODO(hardening): AppSecret at-rest şifrelenmeli.
            b.Property(x => x.AppKey).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
            b.Property(x => x.AppSecret).IsRequired().HasMaxLength(SalesChannelConsts.ConfigMaxLength);
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
    }
}
