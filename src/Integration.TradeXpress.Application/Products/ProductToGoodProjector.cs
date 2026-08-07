using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Variants;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN → MAMÜL PROJEKSİYONU (2026-08-06 Hakan gözlemi): <c>Product</c> ile <c>Good</c>, diğer emtia
/// ailelerinin AKSİNE birbirine neredeyse paralel yürür — altyapıları da zaten ortaktır (aynı jenerik
/// <c>EntityVariant</c> sistemi, aynı DAM medya bağlamı deseni, aynı nitelik grafı).
///
/// <para><b>Neden gerekli:</b> kullanıcı ürüne bakıp aynısını mamül olarak tanımlıyor ve aynı bilgiyi İKİNCİ
/// KEZ giriyor. 1:1 durumda reçete dolaylılığının bedeli tamamen israftır (kazancı çok-emtialı üründe ve
/// aynı mamülü birden çok ürünün paylaşmasında ortaya çıkar). Bu projeksiyon o ikinci girişi kaldırır.</para>
///
/// <para><b>YALNIZ Good'a özeldir — genelleştirilmez.</b> Maden/Mücevher/Taş ürüne paralel DEĞİLDİR; onlar
/// milyem/karat semantiği olan hammaddedir. Aynı projeksiyonu onlara uydurmak, olmayan bir benzerliği
/// varmış gibi göstermek olurdu.</para>
///
/// <para><b>Kaydetmez.</b> Yalnız forma tohum üretir; kullanıcı stok birimi/fiyat gibi Good'a ÖZEL alanları
/// doldurup kendisi kaydeder. Sessizce kayıt açmak, sınıflandırmanın manuel olması kuralını delerdi.</para>
/// </summary>
public class ProductToGoodProjector : ITransientDependency
{
    private const string ProductEntityName = "Product";

    /// <summary>Türkiye'nin GENEL KDV oranı — üründe oran tanımlı değilse başlangıç. İndirimli oran (%10)
    /// istisnadır; ürünlerin çoğunda yanlış başlangıç veriyordu (2026-08-06).</summary>
    private const decimal DefaultVatRate = 20m;

    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IEntityMediaAppService _entityMedia;

    public ProductToGoodProjector(
        IRepository<Product, Guid> productRepository,
        IEntityVariantGraphService entityVariant,
        IEntityMediaAppService entityMedia)
    {
        _productRepository = productRepository;
        _entityVariant = entityVariant;
        _entityMedia = entityMedia;
    }

    /// <summary>Ürünün mamül aynasını üretir (PERSİSTSİZ).</summary>
    public virtual async Task<GoodGetDto> ProjectAsync(Guid productId)
    {
        var product = await _productRepository.FindAsync(productId)
            ?? throw new BusinessException("TradeXpress:Product:NotFound");

        var dto = new GoodGetDto
        {
            Code        = product.Code,
            Name        = product.Name,
            Description = product.Description,
            CompanyId   = product.CompanyId,
            IsActive    = true,

            // Perakende varsayılanı: mamül adet-bazlı ve fiyatı adet üzerinden.
            IsQuantity      = true,
            PriceByQuantity = true,
            PriceTypeChange = true,

            // KDV üründe tanımlıysa ONDAN gelir — kullanıcının pazaryeri için verdiği oran burada da geçerlidir.
            VatPurchaseRate = product.VatRate ?? DefaultVatRate,
            VatSaleRate     = product.VatRate ?? DefaultVatRate,
        };

        // MEDYA BAĞLAM-BAĞLAM KOPYALANIR (2026-08-06 Hakan kararı (a)): iki depo AYRI kalır ve biri
        // diğerinden TÜRETİLMEZ. Kayıt geneli → kayıt geneli, varyant → varyant. Bağlar MediaId ile
        // linklenir, dosya YENİDEN YÜKLENMEZ (medya içeriği değişmez; aynı ContentHash ikinci kayıt açmaz).
        dto.Media = await _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id);

        // Nitelik + varyant grafı ORTAK sistemden okunur (EntityVariant "Product" bağlamı) — Good tarafında
        // aynı tipler kullanıldığı için dönüşüm alan-eşlemesinden ibarettir.
        var graph = await _entityVariant.LoadGraphAsync(ProductEntityName, product.Id);

        dto.Attributes = graph.Attributes
            .Where(a => !a.IsDeleted)
            .Select(a => new EntityAttributeGraphDto
            {
                Name         = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = a.Values
                    .Where(v => !v.IsDeleted)
                    .Select(v => new EntityAttributeValueGraphDto
                    {
                        Value        = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    })
                    .ToList(),
            })
            .ToList();

        // VARYANTLAR TAŞINIR, YENİDEN ÜRETİLMEZ: kartezyeni yeniden kurmak kullanıcının ürün üzerinde
        // yaptığı elemeleri (silinmiş kombinasyonlar) geri getirirdi.
        //
        // GÖRSELLER VARYANTTA YAŞAR (2026-08-06 Hakan düzeltmesi — ilk hâlinde bunu kaçırmıştım): medya
        // kayıt genelinde değil "{Entity}Variant" bağlamında, varyant id'siyle tutulur ve alan paylaşılan
        // EntityVariantGraphDto tabanındadır. Bağlar MediaId ile KOPYALANIR, dosya YENİDEN YÜKLENMEZ —
        // medya içeriği değişmezdir ve aynı içerik zaten ikinci kayıt açmaz (ContentHash).
        dto.Variants = new List<GoodVariantGraphDto>();
        foreach (var v in graph.Variants.Where(x => !x.IsDeleted))
        {
            var projected = new GoodVariantGraphDto
            {
                IsMain      = v.IsMain,
                Code        = v.Code,
                Name        = v.Name,
                Description = v.Description,
                IsActive    = v.IsActive,
                Barcode     = v.Barcode,
                Gtin        = v.Gtin,
                Mpn         = v.Mpn,
            };

            // Kaynak varyantın DB kimliği yoksa (kaydedilmemiş graf düğümü) bağlanacak medya da yoktur.
            if (v.Id != Guid.Empty)
            {
                projected.Media = await _entityMedia.GetForAsync(MediaEntityNames.ProductVariant, v.Id);
            }

            dto.Variants.Add(projected);
        }

        // Hiç varyant yoksa form boş kalmasın — ana varyant KAYIT KODUYLA doğar ("ANAVARYANT" sentinel'i
        // yerine): tek varyant ayrım değildir, ayırt edici bir kod taşımasının anlamı yok.
        if (dto.Variants.Count == 0)
        {
            dto.Variants.Add(new GoodVariantGraphDto
            {
                IsMain   = true,
                Code     = product.Code,
                Name     = product.Name,
                IsActive = true,
            });
        }

        return dto;
    }
}
