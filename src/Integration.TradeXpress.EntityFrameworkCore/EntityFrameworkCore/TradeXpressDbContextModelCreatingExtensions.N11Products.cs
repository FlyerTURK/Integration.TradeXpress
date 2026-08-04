using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>N11 ürün listeleme mapping'i — ürün×kanal listelemesi (company-owned). Kategori attribute + Seyahat özel
/// bilgisi owned-collection → JSON kolonları. Aynı kanalda aynı ürün için ÇOK kayıt olabilir (2026-07-07).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureN11Products(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<SalesChannelTrN11Product>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11Products", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.SellerCode).IsRequired().HasMaxLength(N11ProductConsts.SellerCodeMaxLength);
            b.Property(x => x.CategoryExternalId).IsRequired().HasMaxLength(N11ProductConsts.ExternalIdMaxLength);
            b.Property(x => x.CategoryName).HasMaxLength(N11ProductConsts.CategoryNameMaxLength);
            b.Property(x => x.ShipmentTemplateName).IsRequired().HasMaxLength(N11ProductConsts.ShipmentTemplateNameMaxLength);
            b.Property(x => x.SaleStatus).HasMaxLength(N11ProductConsts.StatusMaxLength);
            b.Property(x => x.ApprovalStatus).HasMaxLength(N11ProductConsts.StatusMaxLength);
            b.Property(x => x.LastError).HasMaxLength(N11ProductConsts.LastErrorMaxLength);
            // Bekleyen REST push task kimliği — sabit kısa alan (nvarchar(max) gereksiz).
            b.Property(x => x.PendingPushTaskId).HasMaxLength(N11ProductConsts.TaskIdMaxLength);
            b.Property(x => x.SellerNote).HasMaxLength(N11ProductConsts.SellerNoteMaxLength);
            b.Property(x => x.Description).HasMaxLength(N11ProductConsts.DescriptionMaxLength);
            b.Property(x => x.GroupItemCode).HasMaxLength(N11ProductConsts.GroupItemCodeMaxLength);
            b.Property(x => x.GroupAttribute).HasMaxLength(N11ProductConsts.GroupAttributeMaxLength);
            b.Property(x => x.ItemName).HasMaxLength(N11ProductConsts.ItemNameMaxLength);

            // Kategori attribute değerleri + Seyahat özel bilgisi → JSON kolonları (owned collection; N11'e push edilir, sorgulanmaz).
            b.OwnsMany(x => x.CategoryAttributes, a =>
            {
                a.ToJson("Attributes");   // kolon adı SABİT — S3 rename şema değiştirmez
                a.Property(p => p.Name).HasMaxLength(N11ProductConsts.CategoryAttributeNameMaxLength);
                a.Property(p => p.Value).HasMaxLength(N11ProductConsts.CategoryAttributeValueMaxLength);
            });
            b.OwnsMany(x => x.SpecialInfo, s =>
            {
                s.ToJson();
                s.Property(p => p.Key).HasMaxLength(N11ProductConsts.SpecialInfoKeyMaxLength);
                s.Property(p => p.Value).HasMaxLength(N11ProductConsts.SpecialInfoValueMaxLength);
            });

            // Varyant-başına N11 SKU kimlik satırları (Faz 1): sellerStockCode dondurma + N11 SKU id/version +
            // push snapshot'ı → JSON kolonu (sipariş→varyant çözümü ürün kaydı üzerinden yapılır, çapraz sorgu yok).
            b.OwnsMany(x => x.Skus, s =>
            {
                s.ToJson();
                s.Property(p => p.SellerStockCode).HasMaxLength(N11ProductConsts.StockCodeMaxLength);
                s.OwnsMany(p => p.AttributeSnapshot, a =>
                {
                    a.Property(p => p.Name).HasMaxLength(N11ProductConsts.CategoryAttributeNameMaxLength);
                    a.Property(p => p.Value).HasMaxLength(N11ProductConsts.CategoryAttributeValueMaxLength);
                });
            });

            // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (2026-07-07 kullanıcı kararı) → normal index.
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant ÖZELLİĞİ (ör. "Renk") — ERP ProductAttribute'ın N11-scope klonu (klon-sonra-ayrış).
        builder.Entity<SalesChannelTrN11ProductAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11ProductAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(N11ProductConsts.AttributeNameMaxLength);

            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrN11ProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Özellik DEĞERİ (ör. "Kırmızı"/"Siyah") — ERP ProductAttributeValue'nun N11-scope klonu.
        builder.Entity<SalesChannelTrN11ProductAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11ProductAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(N11ProductConsts.AttributeValueMaxLength);

            b.HasIndex(x => new { x.TenantId, x.AttributeId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant override BAŞLIĞI (fiyat/stok + marj) — ERP ProductVariant'ın N11-scope özelleştirmesi.
        // null alan = ERP'den devral. Anchor artık BU entity'nin KENDİ Id'si (2026-07-09 kararı, klon-sonra-ayrış);
        // ProductVariantId opsiyonel (null = N11-only kombinasyon) → unique index NULL'ları çakışma saymaz olsa da
        // açık filtre daha okunur/güvenli.
        builder.Entity<SalesChannelTrN11ProductStockItem>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11ProductStockItems", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.OverridePrice).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Margin).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.CombinationSignature).HasMaxLength(N11ProductConsts.CombinationSignatureMaxLength);

            // Kanal-ürün + ERP varyant başına TEK override başlığı — yalnız ProductVariantId doluyken (N11-only
            // satırlarda birden çok null aynı kanal-ürüne bağlanabilir; filtre bu yüzden şart).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrN11ProductId, x.ProductVariantId })
                .IsUnique()
                .HasFilter("[ProductVariantId] IS NOT NULL");

            // Kartezyen motor reconcile anahtarı — kanal-ürün başına TEK satır per imza (ID-bazlı; özellik/değer
            // yeniden adlandırılsa da bozulmaz). Yalnız özellik-kaynaklı satırlarda dolu (legacy ERP-doğrudan null).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrN11ProductId, x.CombinationSignature })
                .IsUnique()
                .HasFilter("[CombinationSignature] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant reçetesi (ERP ProductVariantRecipeLine klonu) — AYRI TABLO (owned değil; türev
        // SelectedLines Id referansları JSON'da kırılgan olur). Hesap motoru (ProductRecipeCostCalculator) ortak.
        builder.Entity<SalesChannelTrN11ProductStockItemRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11ProductStockItemRecipeLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Quantity).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Amount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Factor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.ManualAmount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Description).HasMaxLength(ProductRecipeConsts.DescriptionMaxLength);
            b.Property(x => x.DerivedOperand).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.DerivedSourceLineIds).HasMaxLength(ProductRecipeConsts.DerivedSourceLineIdsMaxLength);

            // Kanal-ürün + override başlığı başına sıralı okuma + company güvenlik filtresi.
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrN11ProductId, x.StockItemId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
