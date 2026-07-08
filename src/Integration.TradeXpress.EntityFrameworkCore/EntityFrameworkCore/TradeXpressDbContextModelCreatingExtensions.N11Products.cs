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
            b.Property(x => x.SellerNote).HasMaxLength(N11ProductConsts.SellerNoteMaxLength);
            b.Property(x => x.Description).HasMaxLength(N11ProductConsts.DescriptionMaxLength);
            b.Property(x => x.GroupItemCode).HasMaxLength(N11ProductConsts.GroupItemCodeMaxLength);
            b.Property(x => x.GroupAttribute).HasMaxLength(N11ProductConsts.GroupAttributeMaxLength);
            b.Property(x => x.ItemName).HasMaxLength(N11ProductConsts.ItemNameMaxLength);

            // Kategori attribute değerleri + Seyahat özel bilgisi → JSON kolonları (owned collection; N11'e push edilir, sorgulanmaz).
            b.OwnsMany(x => x.Attributes, a =>
            {
                a.ToJson();
                a.Property(p => p.Name).HasMaxLength(N11ProductConsts.AttributeNameMaxLength);
                a.Property(p => p.Value).HasMaxLength(N11ProductConsts.AttributeValueMaxLength);
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
                    a.Property(p => p.Name).HasMaxLength(N11ProductConsts.AttributeNameMaxLength);
                    a.Property(p => p.Value).HasMaxLength(N11ProductConsts.AttributeValueMaxLength);
                });
            });

            // Aynı kanalda AYNI ürün için birden fazla kayıt OLABİLİR (2026-07-07 kullanıcı kararı) → normal index.
            b.HasIndex(x => new { x.SalesChannelId, x.ProductId });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });

        // Kanal-özel varyant reçetesi (ERP ProductVariantRecipeLine klonu) — AYRI TABLO (owned değil; türev
        // SelectedLines Id referansları JSON'da kırılgan olur). Hesap motoru (ProductRecipeCostCalculator) ortak.
        builder.Entity<SalesChannelTrN11ProductVariantRecipeLine>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "SalesChannelTrN11ProductVariantRecipeLines", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Quantity).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.Amount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Factor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.PayFactor).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.ManualAmount).HasPrecision(ProductRecipeConsts.AmountPrecision, ProductRecipeConsts.AmountScale);
            b.Property(x => x.Description).HasMaxLength(ProductRecipeConsts.DescriptionMaxLength);
            b.Property(x => x.DerivedOperand).HasPrecision(ProductRecipeConsts.FactorPrecision, ProductRecipeConsts.FactorScale);
            b.Property(x => x.DerivedSourceLineIds).HasMaxLength(ProductRecipeConsts.DerivedSourceLineIdsMaxLength);

            // Kanal-ürün + varyant başına sıralı okuma + company güvenlik filtresi.
            b.HasIndex(x => new { x.TenantId, x.SalesChannelTrN11ProductId, x.ProductVariantId, x.LineOrder });
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
        });
    }
}
