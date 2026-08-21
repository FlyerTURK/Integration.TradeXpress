using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Products;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Metals;

public interface IMetalAppService : ICrudAppService<
    MetalGetDto,
    MetalListDto,
    Guid,
    MetalListRequestDto,
    MetalCreateDto,
    MetalUpdateDto>
{
    /// <summary>Maden süreç paneli combo'su için host‖own kayıtlar (birim düzeni + Factor desc + Code asc).</summary>
    Task<List<MetalListDto>> GetPickerListAsync();

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ; jenerik servise delege).</summary>
    Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input);

    /// <summary>Bir madenin AKTİF varyantları (fiş satırı panelindeki varyant combo'su) — fiyatsız (maden fiyatı milyem/işçilik).</summary>
    Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid metalId);

    /// <summary>Ürün reçetesi panelinde kullanılacak yassılaştırılmış maden varyantları (Metal + Varyant bilgisi).</summary>
    Task<List<MetalVariantLookupDto>> GetVariantLookupAsync();

    /// <summary>Madenin ÜRÜN projeksiyonu (PERSİSTSİZ) — emtia ⇄ ürün köprüsünün GERİ yönü; ortak uygulama
    /// <c>CommodityToProductProjector</c>'dadır (yedi aile aynı sınıfı kullanır).
    /// <para>Kaydetmez, yalnız forma seed üretir: kod/ad/açıklama — varyant taşıyan ailede ayrıca nitelik +
    /// varyant grafı ve medya — taşınır; kategori · reçete · fiyat gibi ürüne ÖZEL alanları kullanıcı doldurup
    /// kendisi kaydeder. <b>Emtianın teknik alanları (milyem/faktör, takip birimi, giriş fiyatı) ve özel kodları
    /// TAŞINMAZ</b> — ürün müşteriye bakar, emtia tekniğe bakar.</para></summary>
    Task<ProductGetDto> ProjectToProductAsync(Guid metalId);
}
