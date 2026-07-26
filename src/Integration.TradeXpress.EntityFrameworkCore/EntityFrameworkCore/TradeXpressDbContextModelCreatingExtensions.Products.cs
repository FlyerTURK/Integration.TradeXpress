using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Substitutions;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Ürün mapping'leri — marketplace-hazır Product çekirdeği (Faz 1, Adım 1).
/// Product = company-owned vitrin; varyantlar agnostik EntityVariant sisteminde yaşar (bkz.
/// <c>ConfigureEntityVariants</c>). Reçete satırı (<c>ProductVariantRecipeLine</c>) varyanta
/// EntityVariant.Id ile bağlanır.</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProducts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Product>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "Products", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(ProductConsts.CodeMaxLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(ProductConsts.DescriptionMaxLength);
            // Görseller — owned collection → JSON kolonu (URL ya da blob; push edilir, sorgulanmaz).
            b.OwnsMany(x => x.Images, i =>
            {
                i.ToJson();
                i.Property(p => p.Url).HasMaxLength(ProductConsts.ImageUrlMaxLength);
                i.Property(p => p.BlobName).HasMaxLength(ProductConsts.ImageBlobNameMaxLength);
                i.Property(p => p.FileName).HasMaxLength(ProductConsts.ImageFileNameMaxLength);
                i.Property(p => p.VariantCode).HasMaxLength(Variants.EntityVariantConsts.VariantCodeMaxLength);
            });

            // Marketplace indirimi (ürün-seviyesi) — tip (enum→int) + değer (18,2) + iş tarihleri.
            b.Property(x => x.DiscountValue).HasPrecision(ProductConsts.SalePricePrecision, ProductConsts.SalePriceScale);

            // Pazaryeri-genel varsayılanlar (kanal-ürünü devralır) — Domestic/Condition/PreparingDay/MaxPurchaseQuantity/
            // CurrencyUnitId konvansiyonla (enum→int, Guid?, int?). Metin alanları + owned özel bilgi:
            b.Property(x => x.ShipmentTemplateName).HasMaxLength(ProductConsts.ShipmentTemplateNameMaxLength);
            // Birleşik ERP kargo şablonu referansı (id-only; nav YOK). Silme-guard sorgusu için indekslenir.
            b.HasIndex(x => x.ShipmentTemplateId);
            b.Property(x => x.SellerNote).HasMaxLength(ProductConsts.SellerNoteMaxLength);
            b.OwnsMany(x => x.SpecialInfo, s =>
            {
                s.ToJson();
                s.Property(p => p.Key).HasMaxLength(ProductConsts.SpecialInfoKeyMaxLength);
                s.Property(p => p.Value).HasMaxLength(ProductConsts.SpecialInfoValueMaxLength);
            });

            b.OwnsMany(x => x.AddOns, a =>
            {
                a.ToJson();
                a.Property(p => p.Note).HasMaxLength(ProductConsts.AddOnNoteMaxLength);
            });

            // Kişiselleştirme (personalization) — talimat max; IsPersonalizable/IsRequired (bool) + CharCountMax (int?)
            // konvansiyonla map'lenir. Kanal-ürünü push'ta bunları devralır (SONRAKİ iş).
            b.Property(x => x.PersonalizationInstructions).HasMaxLength(ProductConsts.PersonalizationInstructionsMaxLength);

            // Varyant modu + Muadil konfigürasyonu (Dilim-3) — VariantMode/ToleranceType enum→int konvansiyonla;
            // miktar/tolerans muadil hassasiyetiyle (N5, SubstitutionGroup tolerans deseni). Grup silme-guard /
            // "hangi ürünler bu grubu kullanıyor" sorguları için id-only referans indekslenir (ShipmentTemplateId deseni).
            b.Property(x => x.SubstitutionTargetQuantity).HasPrecision(
                SubstitutionGroupConsts.ToleranceValuePrecision, SubstitutionGroupConsts.ToleranceValueScale);
            b.Property(x => x.SubstitutionToleranceValue).HasPrecision(
                SubstitutionGroupConsts.ToleranceValuePrecision, SubstitutionGroupConsts.ToleranceValueScale);
            b.HasIndex(x => x.SubstitutionGroupId);
            // Ürün-düzeyi varyant override kümesi — EF primitive-collection → JSON kolonu; Dilim-1
            // SubstitutionGroupItem.IncludedVariantIds deseni birebir (provider-agnostik '[]' default:
            // SQLite (EFCore testleri) N'...' tanımaz; SQL Server ASCII'yi nvarchar'a örtük çevirir).
            b.PrimitiveCollection(x => x.SubstitutionOverrideVariantIds).HasDefaultValueSql("'[]'");

            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        builder.Entity<ProductVariantRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductVariantRecipeLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Quantity).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Amount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Factor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.ManualAmount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Description).HasMaxLength(ProductRecipeConsts.DescriptionMaxLength);

            // Türev/devralan satır (3b): operand N5; kaynak-Id CSV snapshot'ı; taban modu/işlem nullable enum (tinyint, convention).
            b.Property(x => x.DerivedOperand).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.DerivedSourceLineIds).HasMaxLength(ProductRecipeConsts.DerivedSourceLineIdsMaxLength);

            // Varyant reçetesi sıralı okuma (drill LineOrder sırası) + company güvenlik query-filter'ı.
            b.HasIndex(x => new { x.TenantId, x.ProductVariantId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });

            // TERS-ENDEKS (ADR-PRODUCT-ORCHESTRATION): "bu madeni reçetesinde taşıyan varyantlar" araması —
            // maden stoğu değişince (VoucherLine tetiği) etkilenen ürünler buradan bulunur. İndeks olmadan
            // sorgu company içi TAM TARAMA olurdu. CommodityVariantId dahil: varyant-granüler eşleşme
            // (hem değişen varyanta bağlı hem varyantsız/null satırlar yakalanır).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.CommodityProcessType, x.CommodityId, x.CommodityVariantId });
        });
    }
}
