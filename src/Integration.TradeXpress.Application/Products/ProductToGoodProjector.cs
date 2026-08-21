using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN → MAMÜL PROJEKSİYONU (2026-08-06 Hakan gözlemi): <c>Product</c> ile <c>Good</c> birbirine neredeyse
/// paralel yürür — altyapıları da zaten ortaktır (aynı jenerik <c>EntityVariant</c> sistemi, aynı DAM medya
/// bağlam çifti, aynı nitelik grafı).
///
/// <para><b>Neden gerekli:</b> kullanıcı ürüne bakıp aynısını mamül olarak tanımlıyor ve aynı bilgiyi İKİNCİ
/// KEZ giriyor. 1:1 durumda reçete dolaylılığının bedeli tamamen israftır (kazancı çok-emtialı üründe ve
/// aynı mamülü birden çok ürünün paylaşmasında ortaya çıkar). Bu projeksiyon o ikinci girişi kaldırır.</para>
///
/// <para><b>Ortak kod <see cref="ProductCommodityProjectionBuilder"/>'da</b> (2026-08-20): köprü artık yedi
/// ailede de var (CLAUDE.md §6 "ANA KÖPRÜ = Product") ve kimlik/medya/nitelik/varyant + sentinel onarımı ORTAK
/// koddur. Burada kalan tek şey MAMÜLE ÖZEL kısım: perakende fiyat-tipi varsayılanları ve KDV eşlemesi.</para>
///
/// <para><b>Kaydetmez.</b> Yalnız forma seed üretir; kullanıcı stok birimi/fiyat gibi Good'a ÖZEL alanları
/// doldurup kendisi kaydeder. Sessizce kayıt açmak, sınıflandırmanın manuel olması kuralını delerdi.</para>
/// </summary>
public class ProductToGoodProjector : ITransientDependency
{
    /// <summary>Türkiye'nin GENEL KDV oranı — üründe oran tanımlı değilse başlangıç. İndirimli oran (%10)
    /// istisnadır; ürünlerin çoğunda yanlış başlangıç veriyordu (2026-08-06).</summary>
    private const decimal DefaultVatRate = 20m;

    private readonly ProductCommodityProjectionBuilder _builder;

    public ProductToGoodProjector(ProductCommodityProjectionBuilder builder)
    {
        _builder = builder;
    }

    /// <summary>Ürünün mamül projeksiyonunu üretir (PERSİSTSİZ).</summary>
    public virtual async Task<GoodGetDto> ProjectAsync(Guid productId)
    {
        return Map(await _builder.BuildAsync(productId, CommodityProjectionShapes.ForwardShapeOf(ProcessType.Good)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen mamül projeksiyonu (DB'ye gitmez) — kullanıcı ürünü kaydetmeden mamülünü
    /// açtığında da nitelik/varyant/görsel taşınsın diye. Kayıtlı yolla AYNI eşlemeye iner.</summary>
    public virtual Task<GoodGetDto> ProjectDraftAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(Map(_builder.BuildFromDraft(draft, CommodityProjectionShapes.ForwardShapeOf(ProcessType.Good))));
    }

    private static GoodGetDto Map(ProductProjectionSeed seed)
    {
        var dto = new GoodGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            CompanyId   = seed.CompanyId,
            IsActive    = true,

            // Perakende varsayılanı: mamül adet-bazlı ve fiyatı adet üzerinden.
            IsQuantity      = true,
            PriceByQuantity = true,
            PriceTypeChange = true,

            // KDV üründe tanımlıysa ONDAN gelir — kullanıcının pazaryeri için verdiği oran burada da geçerlidir.
            VatPurchaseRate = seed.VatRate ?? DefaultVatRate,
            VatSaleRate     = seed.VatRate ?? DefaultVatRate,
        };

        dto.Media.AddRange(seed.Media);
        dto.Attributes.AddRange(seed.Attributes);
        dto.Variants.AddRange(seed.VariantsAs<GoodVariantGraphDto>());
        return dto;
    }
}
