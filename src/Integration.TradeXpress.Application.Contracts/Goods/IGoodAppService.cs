using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Goods;

public interface IGoodAppService : ICrudAppService<
    GoodGetDto,
    GoodListDto,
    Guid,
    GoodListRequestDto,
    GoodCreateDto,
    GoodUpdateDto>
{
    /// <summary>Mamül süreç paneli combo'su için host + çalışılan şirkete-özel kayıtlar (koda göre sıralı).</summary>
    Task<List<GoodListDto>> GetPickerListAsync(Guid? companyId = null);

    /// <summary>Mamülün ÜRÜN projeksiyonu (PERSİSTSİZ) — <c>ProductAppService.ProjectToGoodAsync</c>'in TERSİ.
    /// <para>Kaydetmez, yalnız forma seed üretir: kullanıcı ürüne özel alanları (kategori, reçete, kargo
    /// desisi) doldurup kendisi kaydeder. Sessizce kayıt açmak, sınıflandırmanın MANUEL olması kuralını
    /// delerdi. <b>Fiyat taşınmaz</b> — mamülde fiyat varyantta yaşar, üründe reçeteden türetilir.</para></summary>
    Task<Products.ProductGetDto> ProjectToProductAsync(Guid goodId);

    /// <summary>Bir mamülün AKTİF varyantları (fiş satırı panelindeki varyant combo'su) — varyant-başı fiyatı (ana varyant
    /// öncelikli, sonra koda göre) ile. Tek varyantlıysa panel VariantId'yi null bırakır; çokluysa fiyatı seçilen varyant belirler.</summary>
    Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid goodId);

    /// <summary>Reçete paneli için TÜM görünür mamül×varyant yassı listesi (Metal <c>GetVariantLookupAsync</c> deseni)
    /// — fiyat SEÇİLİ varyantın <c>GoodVariantDetail</c>'inden. "Varyantlı her emtia reçetede maden gibi davranır"
    /// (2026-08-15 Hakan kararı): satıra <c>CommodityVariantId</c> yazılır, maliyet motoru o varyantın fiyatını okur.</summary>
    Task<List<CommodityVariantLookupDto>> GetVariantLookupAsync();

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ).
    /// Jenerik agnostik sisteme (EntityVariantGraphService) delege eder. Client round-trip; kalıcılaşma Good save'inde.</summary>
    Task<List<GoodVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input);
}
