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

            // Dahil varyantlar (opt-in) — EF primitive-collection → JSON kolonu (nvarchar(max)); sorgulanmaz,
            // yalnız saklanır+UI'da düzenlenir. BOŞ liste = yalnız ANA varyant (statüko). DB default '[]':
            // mevcut satırlar migration'da geçerli JSON ile backfill edilir (boş string parse edilemezdi).
            // Literal PROVIDER-AGNOSTİK ('[]', N-öneksiz): SQLite (EFCore testleri) N'...' sözdizimini tanımaz;
            // SQL Server ASCII köşeli parantezleri nvarchar'a sorunsuz örtük çevirir.
            b.PrimitiveCollection(x => x.IncludedVariantIds).HasDefaultValueSql("'[]'");

            // Grup satırları sıralı okunur (DisplayOrder = tüketim önceliği).
            b.HasIndex(x => new { x.TenantId, x.SubstitutionGroupId, x.DisplayOrder });
            // Aynı maden aynı gruba İKİ KEZ giremez — DB savunma kontrolü (M5 incelemesi; uygulama ön-kontrolü zaten var).
            b.HasIndex(x => new { x.TenantId, x.SubstitutionGroupId, x.MetalId }).IsUnique();
            // Metal silme/temizlik sorguları (hangi gruplar bu madeni kullanıyor).
            b.HasIndex(x => new { x.TenantId, x.MetalId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
