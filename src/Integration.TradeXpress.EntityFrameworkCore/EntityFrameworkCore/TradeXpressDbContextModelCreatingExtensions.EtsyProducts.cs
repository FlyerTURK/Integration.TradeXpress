using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Etsy ürün listeleme mapping'i — ürün×kanal listelemesi (company-owned). Taksonomi varyasyon-DIŞI
/// attribute + etiket/malzeme/kişiselleştirme owned-collection → JSON kolonları. Aynı kanalda aynı ürün için ÇOK
/// kayıt olabilir. N11 <c>ConfigureN11Products</c>'ın birebir ikizi (Etsy alan delta'sıyla).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureEtsyProducts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<SalesChannelEtsyProduct>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsyProducts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.SellerSkuBase).IsRequired().HasMaxLength(SalesChannelEtsyProductConsts.SellerSkuBaseMaxLength);
            b.Property(x => x.SellerNote).HasMaxLength(SalesChannelEtsyProductConsts.SellerNoteMaxLength);
            b.Property(x => x.TitleOverride).HasMaxLength(SalesChannelEtsyProductConsts.TitleOverrideMaxLength);
            b.Property(x => x.DescriptionOverride).HasMaxLength(SalesChannelEtsyProductConsts.DescriptionOverrideMaxLength);
            b.Property(x => x.ListingState).HasMaxLength(SalesChannelEtsyProductConsts.ListingStateMaxLength);
            b.Property(x => x.LastError).HasMaxLength(SalesChannelEtsyProductConsts.LastErrorMaxLength);

            // Taksonomi varyasyon-DIŞI attribute değerleri + kişiselleştirme özel bilgisi → JSON kolonları
            // (owned collection; Etsy'ye push edilir, sorgulanmaz). N11 CategoryAttributes/SpecialInfo deseni AYNEN.
            b.OwnsMany(x => x.ListingAttributes, a =>
            {
                a.ToJson("Attributes");   // kolon adı SABİT — property rename şema değiştirmez
                a.Property(p => p.Name).HasMaxLength(SalesChannelEtsyProductConsts.ListingAttributeNameMaxLength);
                a.Property(p => p.Value).HasMaxLength(SalesChannelEtsyProductConsts.ListingAttributeValueMaxLength);
            });
            b.OwnsMany(x => x.Tags, t =>
            {
                t.ToJson();
                t.Property(p => p.Value).HasMaxLength(SalesChannelEtsyProductConsts.TagMaxLength);
            });
            b.OwnsMany(x => x.Materials, m =>
            {
                m.ToJson();
                m.Property(p => p.Value).HasMaxLength(SalesChannelEtsyProductConsts.MaterialMaxLength);
            });
            // Kişiselleştirme SORULARI. IsRequired/MaxAllowedCharacters 2026-07-28'de eklendi (Etsy'de bu iki ayar
            // soru başınadır) — JSON kolonu olduğu için şema değişmez, eski satırlar varsayılanla okunur.
            b.OwnsMany(x => x.SpecialInfo, s =>
            {
                s.ToJson();
                s.Property(p => p.Key).HasMaxLength(SalesChannelEtsyProductConsts.SpecialInfoKeyMaxLength);
                s.Property(p => p.Value).HasMaxLength(SalesChannelEtsyProductConsts.SpecialInfoValueMaxLength);
            });

            // Varyant-başına Etsy SKU kimlik satırları: FrozenSku dondurma + Etsy product id/version +
            // push snapshot'ı → JSON kolonu (sipariş→varyant çözümü ürün kaydı üzerinden yapılır, çapraz sorgu yok).
            // N11 Skus/AttributeSnapshot deseni AYNEN (AttributeSnapshot→PropertySnapshot).
            b.OwnsMany(x => x.Skus, s =>
            {
                s.ToJson();
                s.Property(p => p.FrozenSku).HasMaxLength(SalesChannelEtsyProductConsts.StockCodeMaxLength);
                s.OwnsMany(p => p.PropertySnapshot, a =>
                {
                    a.Property(p => p.Name).HasMaxLength(SalesChannelEtsyProductConsts.ListingAttributeNameMaxLength);
                    a.Property(p => p.Value).HasMaxLength(SalesChannelEtsyProductConsts.ListingAttributeValueMaxLength);
                });
            });

            // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR → normal index (N11 deseni AYNEN).
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant ÖZELLİĞİ (ör. "Renk") — ERP ProductAttribute'ın Etsy-scope klonu (klon-sonra-ayrış).
        builder.Entity<SalesChannelEtsyProductAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsyProductAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(SalesChannelEtsyProductConsts.AttributeNameMaxLength);

            b.HasIndex(x => new { x.TenantId, x.SalesChannelEtsyProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Özellik DEĞERİ (ör. "Kırmızı"/"Siyah") — ERP ProductAttributeValue'nun Etsy-scope klonu.
        builder.Entity<SalesChannelEtsyProductAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsyProductAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(SalesChannelEtsyProductConsts.AttributeValueMaxLength);

            b.HasIndex(x => new { x.TenantId, x.AttributeId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant override BAŞLIĞI (fiyat/stok + marj) — ERP ProductVariant'ın Etsy-scope özelleştirmesi.
        // null alan = ERP'den devral. Anchor BU entity'nin KENDİ Id'si (klon-sonra-ayrış); ProductVariantId
        // opsiyonel (null = Etsy-only kombinasyon) → unique index NULL'ları çakışma saymaz olsa da açık filtre
        // daha okunur/güvenli. N11 StockItem deseni AYNEN.
        builder.Entity<SalesChannelEtsyProductStockItem>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsyProductStockItems", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.OverridePrice).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Margin).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.CombinationSignature).HasMaxLength(SalesChannelEtsyProductConsts.CombinationSignatureMaxLength);

            // Kanal-ürün + ERP varyant başına TEK override başlığı — yalnız ProductVariantId doluyken (Etsy-only
            // satırlarda birden çok null aynı kanal-ürüne bağlanabilir; filtre bu yüzden şart).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelEtsyProductId, x.ProductVariantId })
                .IsUnique()
                .HasFilter("[ProductVariantId] IS NOT NULL");

            // Kartezyen motor reconcile anahtarı — kanal-ürün başına TEK satır per imza (ID-bazlı; özellik/değer
            // yeniden adlandırılsa da bozulmaz). Yalnız özellik-kaynaklı satırlarda dolu (legacy ERP-doğrudan null).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelEtsyProductId, x.CombinationSignature })
                .IsUnique()
                .HasFilter("[CombinationSignature] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant reçetesi (ERP ProductVariantRecipeLine klonu) — AYRI TABLO (owned değil; türev
        // SelectedLines Id referansları JSON'da kırılgan olur). Hesap motoru (ProductRecipeCostCalculator) ortak.
        // N11 RecipeLine deseni AYNEN.
        builder.Entity<SalesChannelEtsyProductStockItemRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelEtsyProductStockItemRecipeLines", TradeXpressConsts.DbSchema);
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
            b.HasIndex(x => new { x.TenantId, x.SalesChannelEtsyProductId, x.StockItemId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
