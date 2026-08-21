using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.TrendyolProducts;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>Trendyol ürün listeleme mapping'i — ürün×kanal listelemesi (company-owned). Kategori attribute (id-bazlı)
/// owned-collection → JSON kolonu. Aynı kanalda aynı ürün için ÇOK kayıt olabilir (N11 ile aynı 2026-07-07 kararı).</summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureTrendyolProducts(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        // SKU SATIRI: ÖNCE KONVANSİYON KEŞFİ SİLİNİR, SONRA SAHİPLİ (owned) İLAN EDİLİR — sıra önemlidir.
        //
        // NE OLDU: SKU sınıfına yeni alanlar eklendiğinde model kurulumu TAMAMEN düştü —
        // "cannot be configured as owned because it has already been configured as a non-owned".
        // Uygulama açılmıyor, tek bir sorgu çalışmıyor; hata da alanı ekleyen dosyayı değil bu dosyayı
        // gösteriyor. Ölçüm: <c>SalesChannelTrTrendyolProduct.Skus</c> koleksiyonu, biz onu yapılandırmadan
        // ÖNCE EF'in ilişki-keşif konvansiyonu tarafından SIRADAN bir gezinme olarak modele giriyor.
        // Aşağıdaki <c>OwnsMany</c> bu kararı normalde ezip tipi sahipliye çeviriyordu; alanlar eklenince
        // dönüşüm reddedildi.
        //
        // NEDEN İKİ SATIR: <c>Owned&lt;T&gt;()</c> TEK BAŞINA YETMEZ — konvansiyonun kaydı zaten modeldedir
        // ve aynı gerekçeyle reddedilir (denendi, kırmızı). <c>Ignore&lt;T&gt;()</c> o kaydı modelden siler,
        // ardından <c>Owned&lt;T&gt;()</c> tipi sıfırdan ve AÇIKÇA sahipli ilan eder. Böylece yapılandırma
        // konvansiyonun ne bulduğuna bağlı olmaktan çıkar: SKU sınıfına alan eklemek modeli bir daha kıramaz.
        //
        // Domain'deki sınıfa <c>[Owned]</c> attribute'u koymak aynı işi görürdü ama Domain'i EF Core'a
        // bağlardı — katman ihlali (CLAUDE.md §2). Karar bu yüzden EF katmanında yaşıyor.
        //
        // Konvansiyon testi: EF test paketinin TAMAMI. Model kurulamazsa her test kırmızı olur — bu kırılma da
        // zaten oradan çıktı, derlemeden ya da birim testlerden değil.
        builder.Ignore<SalesChannelTrTrendyolProductSku>();
        builder.Owned<SalesChannelTrTrendyolProductSku>();

        // SKU'nun iç koleksiyonu da aynı korumaya girer (aynı kırılma sınıfı: konvansiyon keşfi tipi
        // non-owned kaydedip OwnsMany'yi reddedebilir) — sıra yine Ignore → Owned.
        builder.Ignore<SalesChannelTrTrendyolProductSkuRemoteAxisValue>();
        builder.Owned<SalesChannelTrTrendyolProductSkuRemoteAxisValue>();

        builder.Entity<SalesChannelTrTrendyolProduct>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProducts", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ProductMainId).IsRequired().HasMaxLength(TrendyolProductConsts.ProductMainIdMaxLength);
            // Kategori OPSİYONEL (Trendyol_CategoryOptional, 2026-07-11): eksik/eşleşmeyen kategori NULL kalır;
            // eski "0" sentinel satırları migration'da NULL'a çevrildi. Marka zorunlu kalır.
            b.Property(x => x.CategoryId).HasMaxLength(TrendyolProductConsts.CategoryIdMaxLength);
            b.Property(x => x.CategoryName).HasMaxLength(TrendyolProductConsts.CategoryNameMaxLength);
            b.Property(x => x.BrandId).IsRequired().HasMaxLength(TrendyolProductConsts.BrandIdMaxLength);
            b.Property(x => x.BrandName).HasMaxLength(TrendyolProductConsts.BrandNameMaxLength);
            b.Property(x => x.Description).HasMaxLength(TrendyolProductConsts.DescriptionMaxLength);
            b.Property(x => x.BatchRequestId).HasMaxLength(TrendyolProductConsts.BatchRequestIdMaxLength);
            b.Property(x => x.LastBatchRequestType).HasMaxLength(TrendyolProductConsts.BatchRequestTypeMaxLength);
            b.Property(x => x.Status).HasMaxLength(TrendyolProductConsts.StatusMaxLength);
            b.Property(x => x.LastError).HasMaxLength(TrendyolProductConsts.LastErrorMaxLength);
            b.Property(x => x.DimensionalWeight).HasPrecision(18, 3);

            // Import görüntü alanları (Trendyol_ProductSync): RemoteProductMainId = TRENDYOL'un grup anahtarı
            // (bizim ürettiğimiz ProductMainId'den AYRI — import eşleşme anahtarı); ListPrice = uzak liste fiyatı.
            b.Property(x => x.RemoteProductMainId).HasMaxLength(TrendyolProductConsts.ProductMainIdMaxLength);
            b.Property(x => x.ListPrice).HasPrecision(ProductConsts.SalePricePrecision, ProductConsts.SalePriceScale);

            // Kategori attribute değerleri (id-bazlı) → JSON kolonu (owned collection; Trendyol'a push edilir,
            // sorgulanmaz). Kolon adı SABİT "Attributes" — S6 CategoryAttribute tip rename'i şemayı değiştirmez.
            b.OwnsMany(x => x.Attributes, a =>
            {
                a.ToJson("Attributes");
                a.Property(p => p.CustomValue).HasMaxLength(TrendyolProductConsts.CustomAttributeValueMaxLength);
            });

            // Varyant-başına Trendyol SKU kimlik satırları: barcode dondurma + contentId + push snapshot'ı → JSON kolonu
            // (yeniden-bağlama imzası ürün kaydı üzerinden çözülür, çapraz sorgu yok).
            b.OwnsMany(x => x.Skus, s =>
            {
                s.ToJson();
                s.Property(p => p.Barcode).HasMaxLength(TrendyolProductConsts.BarcodeMaxLength);
                s.Property(p => p.StockCode).HasMaxLength(TrendyolProductConsts.StockCodeMaxLength);
                s.OwnsMany(p => p.AttributeSnapshot);
                s.OwnsMany(p => p.RemoteVariantAttributes, a =>
                    a.Property(p => p.ValueText).HasMaxLength(TrendyolProductConsts.CustomAttributeValueMaxLength));
            });

            // Push emniyet alanları: fiyat bandı, override fiyatla AYNI precision (N11 tarafıyla birebir).
            b.Property(x => x.MinPrice).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.MaxPrice).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);

            // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (N11 ile aynı 2026-07-07 kararı) → normal index.
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant ÖZELLİĞİ (ör. "Renk") — ERP ProductAttribute'ın Trendyol-scope klonu (klon-sonra-ayrış).
        builder.Entity<SalesChannelTrTrendyolProductAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProductAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(TrendyolProductConsts.AttributeNameMaxLength);

            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrTrendyolProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Özellik DEĞERİ (ör. "Kırmızı"/"Siyah") — ERP ProductAttributeValue'nun Trendyol-scope klonu.
        builder.Entity<SalesChannelTrTrendyolProductAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProductAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Value).IsRequired().HasMaxLength(TrendyolProductConsts.AttributeValueMaxLength);

            b.HasIndex(x => new { x.TenantId, x.AttributeId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant override BAŞLIĞI (fiyat/stok + marj) — ERP ProductVariant'ın Trendyol-scope özelleştirmesi.
        // null alan = ERP'den devral. Anchor artık BU entity'nin KENDİ Id'si (N11 portu, klon-sonra-ayrış);
        // ProductVariantId opsiyonel (null = Trendyol-only kombinasyon).
        builder.Entity<SalesChannelTrTrendyolProductStockItem>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProductStockItems", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.OverridePrice).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Margin).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.CombinationSignature).HasMaxLength(TrendyolProductConsts.CombinationSignatureMaxLength);

            // Kanal-ürün + ERP varyant başına TEK override başlığı — yalnız ProductVariantId doluyken (Trendyol-only
            // satırlarda birden çok null aynı kanal-ürüne bağlanabilir; filtre bu yüzden şart).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrTrendyolProductId, x.ProductVariantId })
                .IsUnique()
                .HasFilter("[ProductVariantId] IS NOT NULL");

            // Kartezyen motor reconcile anahtarı — kanal-ürün başına TEK satır per imza (ID-bazlı; özellik/değer
            // yeniden adlandırılsa da bozulmaz). Yalnız özellik-kaynaklı satırlarda dolu (legacy ERP-doğrudan null).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrTrendyolProductId, x.CombinationSignature })
                .IsUnique()
                .HasFilter("[CombinationSignature] IS NOT NULL");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant reçetesi (ERP ProductVariantRecipeLine klonu) — AYRI TABLO (owned değil; türev
        // SelectedLines Id referansları JSON'da kırılgan olur). Hesap motoru (ProductRecipeCostCalculator) ortak.
        builder.Entity<SalesChannelTrTrendyolProductStockItemRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProductStockItemRecipeLines", TradeXpressConsts.DbSchema);
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
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrTrendyolProductId, x.StockItemId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Push GEÇMİŞİ — append-only PushHistory kaydı (N11 eşiyle aynı şekil; yazım anı COMPLETED batch'idir).
        builder.Entity<SalesChannelTrTrendyolProductPushHistory>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrTrendyolProductPushHistories", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Barcode).IsRequired().HasMaxLength(TrendyolProductConsts.BarcodeMaxLength);
            b.Property(x => x.Title).HasMaxLength(TrendyolPushHistoryConsts.TitleMaxLength);
            b.Property(x => x.VariantOptions).HasMaxLength(TrendyolPushHistoryConsts.VariantOptionsMaxLength);
            b.Property(x => x.Images).HasMaxLength(TrendyolPushHistoryConsts.ImagesMaxLength);
            b.Property(x => x.BatchRequestId).HasMaxLength(TrendyolProductConsts.BatchRequestIdMaxLength);
            b.Property(x => x.ErrorMessage).HasMaxLength(TrendyolPushHistoryConsts.ErrorMessageMaxLength);
            b.Property(x => x.ListPrice).HasPrecision(TrendyolPushHistoryConsts.PricePrecision, TrendyolPushHistoryConsts.PriceScale);
            b.Property(x => x.SalePrice).HasPrecision(TrendyolPushHistoryConsts.PricePrecision, TrendyolPushHistoryConsts.PriceScale);

            // "Bu SKU'nun geçmişi" — en yeni önce okunur (delil sorgusunun tek şekli).
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrTrendyolProductId, x.Barcode, x.PushedAtUtc });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
