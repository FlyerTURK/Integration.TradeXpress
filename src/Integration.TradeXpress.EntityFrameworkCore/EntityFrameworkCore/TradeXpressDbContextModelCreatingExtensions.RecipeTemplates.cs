using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Reçete şablonu ("orta reçete") mapping'i — company-owned katalog + satırları. Sayısal hassasiyetler
/// REÇETE SATIRIYLA AYNI sabitlerden gelir (<c>ProductRecipeConsts</c>): şablon uygulanırken değerler düz
/// kopyalanır, farklı ölçek sessiz yuvarlama üretirdi.
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureRecipeTemplates(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<RecipeTemplate>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "RecipeTemplates", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(RecipeTemplateConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(RecipeTemplateConsts.DescriptionMaxLength);

            b.HasMany(x => x.Lines)
                .WithOne()
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ad ŞİRKET başına tekil (kod alanı yok — kimlik addır), soft-delete farkındalı.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Name })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        builder.Entity<RecipeTemplateLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "RecipeTemplateLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Aggregate içi satır: ABP yalnız root'a Id atar; tek kaydetmede birden çok yeni satır olduğunda
            // hepsi boş anahtarla change-tracker'a girip çakışırdı (ProductCategory nitelikleriyle aynı gerekçe).
            b.Property(x => x.Id).ValueGeneratedOnAdd();

            b.Property(x => x.Quantity).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Amount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Factor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.DerivedOperand).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Description).HasMaxLength(RecipeTemplateConsts.LineDescriptionMaxLength);

            b.HasIndex(x => new { x.TemplateId, x.LineOrder });
        });
    }
}
