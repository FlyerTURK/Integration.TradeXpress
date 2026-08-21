using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// ÜRÜN → EMTİA PROJEKSİYONLARI (Mamül DIŞINDAKİ altı aile) — <see cref="ProductToGoodProjector"/>'ın kardeşi.
///
/// <para><b>Neden yedi aile de gerekiyor</b> (CLAUDE.md §6 "ANA KÖPRÜ = Product", 2026-08-20): emtia ile kanal
/// ürünü arasında doğrudan yol yoktur, her çevrim Product'tan geçer. Köprünün yalnız Mamül kolunun
/// (<see cref="ProductToGoodProjector"/>) zengin olması bir "detay" değil, köprünün kendisindeki boşluktu: kullanıcı ürüne bakıp aynı bilgiyi (nitelik,
/// varyant, görsel) emtia formuna İKİNCİ KEZ giriyordu.</para>
///
/// <para><b>Neden TEK sınıf, aile başına TİPLİ metot:</b> tipsiz tek endpoint (<c>Task&lt;object&gt;</c>) sözleşmeyi
/// kaybettirirdi — çağıran hangi DTO'yu aldığını derleyiciden değil belgeden öğrenirdi. Altı ayrı sınıf ise
/// aile-kategorisi tablosunu (kim varyant taşır, kim taşımaz) altı dosyaya dağıtırdı; burada yan yana durur ve
/// bir sapma göze çarpar.</para>
///
/// <para><b>HER AİLENİN İKİ GİRİŞİ VAR (2026-08-20):</b> <c>ProjectTo*Async(Guid)</c> KAYITLI üründen,
/// <c>ProjectDraftTo*Async(ProductDraftSeedDto)</c> ise kullanıcının AÇIK FORMUNDAKİ kayıtsız üründen seed üretir.
/// İkisi de aynı <c>MapTo*</c> metoduna iner — aile eşlemesi TEK yerde kalsın diye; iki ayrı eşleme yazmak,
/// bir alanın (ör. Oem) yalnız bir yolda taşınması demekti ve bu tam olarak bu köprüde yaşandı.</para>
///
/// <para><b>TEKNİK ve ÖZEL KOD alanları HİÇBİR ailede doldurulmaz</b> — milyem/Factor, takip birimi, giriş
/// fiyatı, stok birimi, ÖTV/stopaj, marj emtianın kendi alanlarıdır; Marka/Model/Cins/Tür/Renk/Beden/Kategori/
/// Grup kullanıcının gruplama düzenidir. İkisi de DTO'nun kendi varsayılanında bırakılır (CLAUDE.md §6).</para>
///
/// <para><b>KAYDETMEZ</b> — yalnız forma seed üretir.</para>
/// </summary>
public class ProductToCommodityProjector : ITransientDependency
{
    private readonly ProductCommodityProjectionBuilder _builder;

    public ProductToCommodityProjector(ProductCommodityProjectionBuilder builder)
    {
        _builder = builder;
    }

    // ── MADEN (① tam varyantlı) ─────────────────────────────────────────────────────────────────────

    /// <summary>Ürünün MADEN projeksiyonu — tam varyantlı aile: nitelik + varyant grafı ve iki bağlam medya taşınır.
    /// <c>Factor</c> (milyem), <c>FollowingUnitId</c>, <c>StableQuantity</c> gibi TEKNİK alanlar DTO'nun kendi
    /// varsayılanında kalır; onları kullanıcı maden formunda verir.</summary>
    public virtual async Task<MetalGetDto> ProjectToMetalAsync(Guid productId)
    {
        return MapToMetal(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Metal)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen MADEN projeksiyonu (DB'ye gitmez) — kullanıcı ürünü kaydetmeden emtiasını
    /// açtığında da nitelik/varyant/görsel taşınsın diye.</summary>
    public virtual Task<MetalGetDto> ProjectDraftToMetalAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToMetal(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Metal))));
    }

    private static MetalGetDto MapToMetal(ProductProjectionSeed seed)
    {
        var dto = new MetalGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            CompanyId   = seed.CompanyId,
            IsActive    = true,
        };

        dto.Media.AddRange(seed.Media);
        dto.Attributes.AddRange(seed.Attributes);
        dto.Variants.AddRange(seed.VariantsAs<MetalVariantGraphDto>());
        return dto;
    }

    // ── MÜCEVHER (② uzantısız varyantlı) ────────────────────────────────────────────────────────────

    /// <summary>Ürünün MÜCEVHER projeksiyonu — uzantısız varyantlı aile: varyant grafı TAŞINIR ama aileye özel alan
    /// varyantta yaşamaz (fiyat entity seviyesindedir ve köprüden geçmez).</summary>
    public virtual async Task<JewelryGetDto> ProjectToJewelryAsync(Guid productId)
    {
        return MapToJewelry(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Jewelry)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen MÜCEVHER projeksiyonu (DB'ye gitmez).</summary>
    public virtual Task<JewelryGetDto> ProjectDraftToJewelryAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToJewelry(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Jewelry))));
    }

    private static JewelryGetDto MapToJewelry(ProductProjectionSeed seed)
    {
        var dto = new JewelryGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            CompanyId   = seed.CompanyId,
            IsActive    = true,
        };

        dto.Media.AddRange(seed.Media);
        dto.Attributes.AddRange(seed.Attributes);
        dto.Variants.AddRange(seed.VariantsAs<EntityVariantGraphDto>());
        return dto;
    }

    // ── TAŞ (③ varyantsız, ama kayıt-geneli medya taşır) ────────────────────────────────────────────

    /// <summary>Ürünün TAŞ projeksiyonu — VARYANTSIZ aile (2026-08-09: "her taşın parmak izi ayrıdır").
    ///
    /// <para><b>DTO'da <c>Variants</c>/<c>Attributes</c> alanları DURUYOR ama BİLİNÇLE BOŞ bırakılır:</b> taş
    /// kaydında varyant sekmesi kaldırıldı ve <c>StoneAppService.SaveGraphAsync</c> varyant kolunu atlıyor.
    /// Alanın varlığı onu doldurmak için bir gerekçe değildir — doldurmak "eksikliği" gidermez, TASARIMI
    /// bozardı. Medya kayıt geneli olarak taşınır (taşın da fotoğrafı vardır).</para></summary>
    public virtual async Task<StoneGetDto> ProjectToStoneAsync(Guid productId)
    {
        return MapToStone(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Stone)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen TAŞ projeksiyonu (DB'ye gitmez) — yalnız kimlik + kayıt-geneli medya.</summary>
    public virtual Task<StoneGetDto> ProjectDraftToStoneAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToStone(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Stone))));
    }

    private static StoneGetDto MapToStone(ProductProjectionSeed seed)
    {
        var dto = new StoneGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            CompanyId   = seed.CompanyId,
            IsActive    = true,
        };

        dto.Media.AddRange(seed.Media);
        return dto;
    }

    // ── HURDA · VADELİ · HİZMET (③ varyantsız, medyasız — yalnız kimlik) ────────────────────────────

    /// <summary>Ürünün HURDA projeksiyonu — varyantsız ve DTO'sunda medya/nitelik/varyant alanı YOK: yalnız kimlik
    /// taşınır. <c>Factor</c> (milyem) teknik alandır, dokunulmaz.</summary>
    public virtual async Task<ScrapGetDto> ProjectToScrapAsync(Guid productId)
    {
        return MapToScrap(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Scrap)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen HURDA projeksiyonu (DB'ye gitmez).</summary>
    public virtual Task<ScrapGetDto> ProjectDraftToScrapAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToScrap(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Scrap))));
    }

    private static ScrapGetDto MapToScrap(ProductProjectionSeed seed)
    {
        return new ScrapGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            IsActive    = true,
        };
    }

    /// <summary>Ürünün VADELİ projeksiyonu — "vadeli varyant barındırmaz" (Hakan, 2026-08-08). Yalnız kimlik;
    /// <c>FollowingFactor</c> ve takip birimi teknik alanlardır.</summary>
    public virtual async Task<FutureGetDto> ProjectToFutureAsync(Guid productId)
    {
        return MapToFuture(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Future)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen VADELİ projeksiyonu (DB'ye gitmez).</summary>
    public virtual Task<FutureGetDto> ProjectDraftToFutureAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToFuture(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Future))));
    }

    private static FutureGetDto MapToFuture(ProductProjectionSeed seed)
    {
        return new FutureGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            IsActive    = true,
        };
    }

    /// <summary>Ürünün HİZMET projeksiyonu — hizmet stoklanan emtia değil, reçeteye giren ÜCRET kalemidir;
    /// stoklanmayanın varyantı da görseli de olmaz. Yalnız kimlik taşınır.</summary>
    public virtual async Task<ServiceGetDto> ProjectToServiceAsync(Guid productId)
    {
        return MapToService(await _builder.BuildAsync(productId, ShapeOf(ProcessType.Service)));
    }

    /// <summary>KAYDEDİLMEMİŞ üründen HİZMET projeksiyonu (DB'ye gitmez).</summary>
    public virtual Task<ServiceGetDto> ProjectDraftToServiceAsync(ProductDraftSeedDto draft)
    {
        return Task.FromResult(MapToService(_builder.BuildFromDraft(draft, ShapeOf(ProcessType.Service))));
    }

    private static ServiceGetDto MapToService(ProductProjectionSeed seed)
    {
        return new ServiceGetDto
        {
            Code        = seed.Code,
            Name        = seed.Name,
            Description = seed.Description,
            IsActive    = true,
        };
    }

    /// <summary>Ailenin projeksiyon şekli — TEK tablodan (<see cref="CommodityProjectionShapes"/>). Kayıtlı ve
    /// taslak yol AYNI şekli okur; taslağa ayrı bir sınıflandırma vermek iki yolun sessizce ayrışması demekti.</summary>
    private static ProductProjectionShape ShapeOf(ProcessType family)
    {
        return CommodityProjectionShapes.ForwardShapeOf(family);
    }
}
