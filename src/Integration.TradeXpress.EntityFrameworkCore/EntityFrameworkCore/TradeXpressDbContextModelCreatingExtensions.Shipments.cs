using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Shipments;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Birleşik ERP kargo şablonu (<see cref="ShipmentTemplate"/>) + çekirdek kargo firması
/// (<see cref="Carrier"/>) mapping'i. Şablon company-owned + per-tenant (menşei/iade adresleri Address VO,
/// <c>ConfigureAddress</c> helper'ı N11Shipments partial'ında paylaşılır; Code company-scoped benzersiz).
/// Carrier <b>host-global</b> (IMultiTenant değil → TenantId kolonu yok; Geography deseni) — Code global benzersiz.</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureShipmentTemplates(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ShipmentTemplate>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ShipmentTemplates", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(ShipmentTemplateConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(ShipmentTemplateConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(ShipmentTemplateConsts.DescriptionMaxLength);
            b.Property(x => x.CarrierName).HasMaxLength(ShipmentTemplateConsts.CarrierNameMaxLength);
            b.Property(x => x.ReturnInfo).HasMaxLength(ShipmentTemplateConsts.ReturnInfoMaxLength);

            // Şartlı kargo eşiği — şablon-düzeyinde skaler (yalnız Conditional'da dolu).
            b.Property(x => x.ConditionalThreshold)
                .HasPrecision(ShipmentTemplateConsts.ThresholdPrecision, ShipmentTemplateConsts.ThresholdScale);

            // Adresler = yeniden-kullanılabilir Address VO (OwnsOne; aynı tabloda prefix'li kolonlar). Gönderim ve iade
            // adresleri artık ikisi de OPSİYONEL (şube modunda gömülü adres yok) — City/Line required (owned) → EF
            // null-tespiti (tüm kolonlar null → adres null). Şube modu ayrı DispatchBranchId/ReturnBranchId kolonlarında.
            b.OwnsOne(x => x.DispatchAddress, ConfigureAddress);
            b.OwnsOne(x => x.ReturnAddress, ConfigureAddress);

            // Kimlik = (TenantId, CompanyId, Code) — company-scoped benzersiz, soft-delete kayıtları hariç.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }

    /// <summary>Çekirdek kargo firması (<see cref="Carrier"/>) mapping'i — <b>host-global</b> (IMultiTenant DEĞİL →
    /// TenantId kolonu yok; Geography/N11City deseni). Code global benzersiz (yalnız soft-delete edilmemiş satırlar).</summary>
    public static void ConfigureCarriers(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Carrier>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Carriers", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(CarrierConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(CarrierConsts.NameMaxLength);

            // Code host-global benzersiz — soft-delete edilmiş satır hariç (N11City CityCode deseni).
            b.HasIndex(x => x.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        });
    }
}
