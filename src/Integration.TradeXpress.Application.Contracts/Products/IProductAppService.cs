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
}
