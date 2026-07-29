using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Integration.TradeXpress.ProductCategories;
using Integration.TradeXpress.Variants;

namespace Integration.TradeXpress.EntityFrameworkCore;

/// <summary>
/// Çekirdek ürün kategorisi mapping'i — company-owned ağaç + nitelik/değer AYRI TABLOLAR.
///
/// <para><b>Neden JSON değil tablo:</b> nitelik ve değer ileride pazaryeri niteliğine/değerine eşleştirilecek;
/// eşleştirme kalıcı bir <c>Id</c>'ye asılır. Owned-JSON'da satırın kimliği yoktur (sıra/ad değişince eşleştirme
/// sessizce kayar) — bu yüzden <c>N11Product.CategoryAttributes</c> JSON'dur (push payload'ı, referanslanmaz),
/// burası tablodur (referans hedefi).</para>
///
/// <para><b>Ağaç:</b> <c>ParentId</c> id-only self-referans — sert FK YOK. Sebep: ağaç bütünlüğü zaten
/// <c>ProductCategoryTreeManager</c>'da (döngü + derinlik + aynı şirket) korunuyor ve sert FK, soft-delete edilmiş
/// bir ebeveynin altındaki dalı fiziksel silmede kilitlerdi. Index ağaç sorgusu içindir.</para>
/// </summary>
public static partial class TradeXpressDbContextModelCreatingExtensions
{
    public static void ConfigureProductCategories(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<ProductCategory>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductCategories", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(ProductCategoryConsts.NameMaxLength);
            b.Property(x => x.Description).HasMaxLength(ProductCategoryConsts.DescriptionMaxLength);

            b.HasMany(x => x.Attributes)
                .WithOne()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // Benzersizlik KARDEŞ düzeyinde: aynı üst altında aynı ad iki kez olamaz — "Takı › Yüzük" ve
            // "Saat › Yüzük" ikisi de meşrudur. Kökler ParentId=NULL ile aynı kovada; SQL Server unique index'te
            // NULL'ları eşit saydığından iki kök aynı adı da alamaz (istenen davranış). Soft-delete farkındalı:
            // silinen bir kategorinin adı yeniden kullanılabilsin.
            b.HasIndex(x => new { x.TenantId, x.CompanyId, x.ParentId, x.Name })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        builder.Entity<ProductCategoryAttribute>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductCategoryAttributes", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Id'yi EF ekleme anında üretir. ABP yalnız AGGREGATE ROOT'a Id atar; aggregate içi satırlar
            // SaveChanges'e kadar Guid.Empty kalır ve TEK kaydetmede iki yeni satır olduğunda ikisi de aynı
            // (boş) anahtarla change-tracker'a girip "another instance with the same key value" hatası verir.
            // Somut belirtisi: iki değerli bir özelliği olan YENİ kategori kaydedilemez. Ctor'a Guid id koymak
            // konvansiyon yasağı (EntityConventionTests allow-list'i yeni entity kabul etmez), o yüzden anahtar
            // üretimi buraya alınır. Kolon tanımı değişmez — yalnız değerin nereden geldiği değişir.
            b.Property(x => x.Id).ValueGeneratedOnAdd();

            // Uzunluklar agnostik nitelik sistemiyle AYNI sabitten — kategori niteliği ürünün nitelik grafına
            // yansıyacağı için daha geniş bir sınır yansıma anında sessiz kırpılmaya yol açardı.
            b.Property(x => x.Name).IsRequired().HasMaxLength(EntityVariantConsts.AttributeNameMaxLength);

            b.HasMany(x => x.Values)
                .WithOne()
                .HasForeignKey(x => x.AttributeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Aynı kategoride iki "Renk" olamaz. CategoryId zaten tenant+şirket kapsamını taşır → ayrıca TenantId'ye gerek yok.
            b.HasIndex(x => new { x.CategoryId, x.Name })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });

        builder.Entity<ProductCategoryAttributeValue>(b =>
        {
            b.ToTable(TradeXpressConsts.DbTablePrefix + "ProductCategoryAttributeValues", TradeXpressConsts.DbSchema);
            b.ConfigureByConvention();

            // Nitelikteki ile aynı gerekçe (yukarıda): tek kaydetmede birden çok yeni değer eklenebilir.
            b.Property(x => x.Id).ValueGeneratedOnAdd();

            b.Property(x => x.Value).IsRequired().HasMaxLength(EntityVariantConsts.AttributeValueMaxLength);

            // Aynı nitelikte iki "14K" olamaz.
            b.HasIndex(x => new { x.AttributeId, x.Value })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");
        });
    }
}
