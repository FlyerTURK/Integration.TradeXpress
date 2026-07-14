using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Agnostik varyant sistemi mapping'leri — EntityAttribute / EntityAttributeValue / EntityVariant + varyant↔değer bağı.
/// SpecialCode/EntityImage agnostik deseniyle hizalı: TEK tablo seti tüm entity'lere (EntityName+EntityId) hizmet eder.
/// Scope index'leri (EntityName, EntityId) üzerinden; id-only bağlar (sert FK yok — silme cascade'i servis/manager'da).
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureEntityVariants(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<EntityVariant>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityVariants", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(EntityVariantConsts.EntityNameMaxLength);
            b.Property(x => x.Code).IsRequired().HasMaxLength(EntityVariantConsts.VariantCodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EntityVariantConsts.VariantNameMaxLength);
            b.Property(x => x.Description).HasMaxLength(EntityVariantConsts.DescriptionMaxLength);
            b.Property(x => x.Barcode).HasMaxLength(EntityVariantConsts.BarcodeMaxLength);
            b.Property(x => x.Gtin).HasMaxLength(EntityVariantConsts.TradeIdentifierMaxLength);
            b.Property(x => x.Mpn).HasMaxLength(EntityVariantConsts.TradeIdentifierMaxLength);
            b.Property(x => x.Oem).HasMaxLength(EntityVariantConsts.TradeIdentifierMaxLength);

            // Varyant kodu SAHİP (EntityName+EntityId) başına tekil.
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            // Ana varyant araması (tek-main invariant).
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.IsMain });
        });

        builder.Entity<EntityAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.EntityName).IsRequired().HasMaxLength(EntityVariantConsts.EntityNameMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(EntityVariantConsts.AttributeNameMaxLength);

            // Nitelik adı SAHİP başına tekil (aynı sahipte iki "Renk" olamaz).
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.Name }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<EntityAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(EntityVariantConsts.AttributeValueMaxLength);

            // Değer NİTELİK başına tekil.
            b.HasIndex(x => new { x.TenantId, x.EntityAttributeId, x.Value }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<EntityVariantAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "EntityVariantAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Varyant başına nitelik başına TEK değer (kombinasyon değişmezi).
            b.HasIndex(x => new { x.TenantId, x.EntityVariantId, x.EntityAttributeId }).IsUnique();
            // Değer-bazlı temizlik.
            b.HasIndex(x => new { x.TenantId, x.EntityAttributeValueId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
