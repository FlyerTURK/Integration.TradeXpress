using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Substitutions;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Muadil (Substitution) mapping'leri — SubstitutionGroup (company-owned başlık + tolerans
/// politikası) ve SubstitutionGroupItem (ayrı aggregate, id-only referans; sıra = tüketim önceliği).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureSubstitutions(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<SubstitutionGroup>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SubstitutionGroups", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(SubstitutionGroupConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(SubstitutionGroupConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(SubstitutionGroupConsts.DescriptionMaxLength);
            // Tolerans değeri — gram ya da binde (N5, Metal.StableQuantity hassasiyetiyle hizalı).
            b.Property(x => x.ToleranceValue).HasPrecision(
                SubstitutionGroupConsts.ToleranceValuePrecision, SubstitutionGroupConsts.ToleranceValueScale);

            // Grup kodu ŞİRKET başına tekil (Product deseni; AppService ön-kontrolü M3'te).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            // Company güvenlik query-filter'ını hızlandırır (ICompanyOwned).
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<SubstitutionGroupItem>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SubstitutionGroupItems", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Grup satırları sıralı okunur (DisplayOrder = tüketim önceliği).
            b.HasIndex(x => new { x.TenantId, x.SubstitutionGroupId, x.DisplayOrder });
            // Aynı maden aynı gruba İKİ KEZ giremez — DB emniyet kemeri (M5 incelemesi; uygulama ön-kontrolü zaten var).
            b.HasIndex(x => new { x.TenantId, x.SubstitutionGroupId, x.MetalId }).IsUnique();
            // Metal silme/temizlik sorguları (hangi gruplar bu madeni kullanıyor).
            b.HasIndex(x => new { x.TenantId, x.MetalId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
