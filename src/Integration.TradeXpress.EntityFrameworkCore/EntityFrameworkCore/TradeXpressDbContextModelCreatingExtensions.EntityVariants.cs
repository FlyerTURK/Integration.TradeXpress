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

            // Varyant kodu SAHİP (EntityName+EntityId) başına tekil — SOFT-DELETE FARKINDALI (2026-08-07).
            // Öncesinde silinmiş varyantın kodu kalıcı işgal ediliyordu; aynı sahibin yeniden kurulan varyantı
            // "-2" son eki almak zorunda kalıyordu. Aynı tablodaki TEK-ANA indeksi zaten "IsDeleted = 0" taşıyor
            // → bu satır o desene hizalanır.
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.Code }).IsUnique()
                .HasFilter("[TenantId] IS NOT NULL AND [IsDeleted] = 0");
            b.HasIndex(x => new { x.TenantId, x.CompanyId });
            // Ana varyant araması (tek-main invariant).
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.IsMain });
            // TEK-ANA değişmezinin DB-backstop'u (2026-07-25 inceleme bulgusu #18): EntityVariantManager
            // UnsetOtherMainsAsync ile korur ama eşzamanlı iki materyalizasyon/elle edit yarışı iki IsMain
            // bırakabilirdi — filtreli unique yarış kazasını DB'de keser (canlı satırlar arasında tek ana).
            b.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId })
                .IsUnique()
                .HasFilter("[IsMain] = 1 AND [IsDeleted] = 0")
                .HasDatabaseName("IX_AppEntityVariants_SingleMain");

            // Barkod PRODUCT varyantlarında ŞİRKET İÇİNDE tekil — pazaryeri (N11/Trendyol) idempotent import'un
            // DB-backstop'u (eski ProductVariant'ta vardı; Product→agnostik geçişte istemeden düşmüştü → çift-import/race
            // açığı). YALNIZ "Product" ile filtrelenir (Good/Metal/Stone barkodları etkilenmez); null barkod hariç.
            // SQLite kısmi-index'i de bu filtreyi destekler.
            //
            // KAPSAM: eskiden (TenantId, Barcode) idi — tenant genelinde. 2026-08-04'te CompanyId eklendi çünkü o
            // hâli sahiplik modeliyle ÇELİŞİYORDU (CLAUDE.md §6: emtia katalogları ve Product per-company) ve
            // gerçek bir iş senaryosunu blokluyordu: aynı tenant altında birden çok şirket, her biri kendi
            // pazaryeri kanalıyla AYNI barkodlu malı satabilmeli. Barkod (EAN/UPC) malın küresel kimliğidir —
            // "kimin stoğunda" sorusunu cevaplamaz, o yüzden tekillik şirket sınırında olmalıdır.
            //
            // IsDeleted FİLTRESİ: soft-delete edilmiş satır eskiden barkodu SÜRESİZ işgal ediyordu. İçe aktarımın
            // barkod araması soft-delete filtresine tabi olduğundan silinmiş satırı GÖREMİYOR, barkodu boş sanıp
            // INSERT deniyor ve indeks ihlaliyle TÜM içe aktarım düşüyordu. Filtre artık indeksle aramayı hizalıyor.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.Barcode }).IsUnique()
                .HasFilter("[EntityName] = 'Product' AND [Barcode] IS NOT NULL AND [IsDeleted] = 0");
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
