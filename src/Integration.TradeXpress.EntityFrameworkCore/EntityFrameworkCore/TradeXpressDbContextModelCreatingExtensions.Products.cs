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

            // core kategori bağı — id-only (sert FK YOK: kategori silme guard'ı AppService'te, ve sert FK
            // soft-delete edilmiş bir kategoriyi fiziksel silmede kilitlerdi). Index, "bu kategoriye bağlı ürün
            // var mı" silme-guard'ı ve kategori bazlı listeleme içindir.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ProductCategoryId });
            // Marketplace indirimi (ürün-seviyesi) — tip (enum→int) + değer (18,2) + iş tarihleri.
            b.Property(x => x.DiscountValue).HasPrecision(ProductConsts.SalePricePrecision, ProductConsts.SalePriceScale);

            // Pazaryeri-genel varsayılanlar (kanal-ürünü devralır) — Domestic/Condition/PreparingDay/MaxPurchaseQuantity/
            // CurrencyUnitId konvansiyonla (enum→int, Guid?, int?). Metin alanları + owned özel bilgi:
            // Birleşik ERP kargo şablonu referansı (id-only; nav YOK). Silme-guard sorgusu için indekslenir.
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

            // SOFT-DELETE FARKINDALI (2026-08-07 — Hakan bulgusu). Öncesinde filtre yalnız "[TenantId] IS NOT NULL"
            // idi ve SİLİNMİŞ ürün kodunu KALICI olarak işgal ediyordu: mağazadan yeniden içe aktarılan ürün
            // orijinal stok koduyla değil "-2" son ekiyle kaydediliyordu. Bu, ev kuralından bir SAPMAYDI — kardeş
            // katalogların (Good/Metal/Jewelry/Stone/Scrap/Future/Service) hepsi zaten "IsDeleted = 0" taşıyor.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Code }).IsUnique()
                .HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0");
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

            // Otomatik yenileme "yalnız kendi ürettiği satırları" siler (Origin filtresi) — bu index o silmenin
            // ve imza okumasının yoludur. Varsayılan 0 (Manual): mevcut satırlar kullanıcı satırı sayılır, yani
            // muadil yenilemesi eski satırlara artık DOKUNMAZ (geriye dönük güvenli taraf).
            b.Property(x => x.Origin).HasDefaultValue(RecipeLineOrigin.Manual);
            b.HasIndex(x => new { x.TenantId, x.ProductVariantId, x.Origin });

            // Şablon soy kimliği (2026-08-21 çoğalma düzeltmesi): yeniden uygulamanın eşleme anahtarı —
            // null = şablondan gelmedi ya da özellik öncesi eski satır (eski davranış geçerli). FK YOK (şablon
            // ürünle kalıcı bağ kurmaz; şablon silinse de satır yaşar) ve İNDEKS YOK: eşleme, varyantın zaten
            // yüklenmiş satırları üzerinde bellek içinde yapılır — hiçbir sorgu bu kolonla filtrelemez.
            b.Property(x => x.SourceTemplateLineId);

            // TERS-ENDEKS (ADR-PRODUCT-ORCHESTRATION): "bu madeni reçetesinde taşıyan varyantlar" araması —
            // maden stoğu değişince (VoucherLine tetiği) etkilenen ürünler buradan bulunur. İndeks olmadan
            // sorgu company içi TAM TARAMA olurdu. CommodityVariantId dahil: varyant-granüler eşleşme
            // (hem değişen varyanta bağlı hem varyantsız/null satırlar yakalanır).
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.CommodityProcessType, x.CommodityId, x.CommodityVariantId });
        });
    }
}
