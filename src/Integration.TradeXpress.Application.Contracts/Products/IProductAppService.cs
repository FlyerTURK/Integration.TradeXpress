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
}
