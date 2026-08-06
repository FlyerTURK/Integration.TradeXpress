using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Products;

public interface IProductAppService : ICrudAppService<
    ProductGetDto,
    ProductListDto,
    Guid,
    ProductListRequestDto,
    ProductCreateDto,
    ProductUpdateDto>
{
    /// <summary>Nitelik grafından varyantları ÜRETİR — PERSISTSİZ önizleme (kayıt gerekmez, DB'ye yazmaz).
    /// Kartezyen + kod/ad türetme <c>ProductVariantSynchronizer</c> ile AYNI mantık; ilk satır IsMain (display),
    /// hepsi aktif, <c>CombinationKey</c> dolu. Değersiz nitelik → <c>TradeXpress:ProductAttribute:ValueRequired</c>.</summary>
    Task<List<ProductVariantGraphDto>> GenerateVariantsAsync(ProductVariantGenerateRequestDto input);

    /// <summary>Bir varyantın reçete satırlarının CANLI maliyetini PERSISTSİZ hesaplar (TAM KAYIT gerekmez) —
    /// <c>GetAsync</c> projeksiyonuyla AYNI motor (ülke birimine rebase + calculator). Satır-başı Uygulanacak Bedel /
    /// Satır Maliyeti / Ara Toplam + varyant net'i döner. DB'ye YAZMAZ (design-time; ledger'a değmez).</summary>
    Task<ProductRecipeCostResultDto> CalculateRecipeCostAsync(ProductRecipeCostRequestDto input);

    /// <summary>
    /// Ürünün genel özelliklerini bir KANALIN nitelik alanlarına çevirir (kategori eşleştirmeleri üzerinden).
    /// Kanal ürünü kurulurken nitelikler bununla ön-doldurulur — kullanıcı aynı bilgiyi ikinci kez girmesin.
    /// </summary>
    Task<List<ProductChannelAttributeDto>> ResolveChannelAttributesAsync(ProductChannelAttributeResolveDto input);

    /// <summary>Çalışılan şirketteki SINIFLANDIRILMAMIŞ ürünler — reçetesi hiç olmayanlar. Sihirbazın
    /// sınıflandırma adımı bu listeyi doldurur.
    /// <para><b>Kanal parametresi YOK</b> (bilinçli): liste kanaldan değil ŞİRKETTEN çıkar, böylece adım
    /// eski içe aktarımların bıraktığı ürünleri de yakalar — yalnız "bu turda gelenler"e bakmak, geçmişte
    /// atlanmış ürünleri sonsuza dek görünmez kılardı.</para></summary>
    Task<List<ProductCommodityCandidateDto>> GetUnclassifiedProductsAsync();

    /// <summary>Sihirbaz sınıflandırmasını uygular: emtia kaydı (gerekiyorsa) → reçete satırı → stok
    /// politikası → otorite devri → stok yeniden-hesap job'ı. TEK çağrı (ürün başına round-trip YOK).</summary>
    Task<ProductCommodityProvisionResultDto> ProvisionCommoditiesAsync(ProductCommodityProvisionInputDto input);
}
