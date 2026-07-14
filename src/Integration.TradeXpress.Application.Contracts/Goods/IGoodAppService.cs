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

    /// <summary>Bir mamülün AKTİF varyantları (fiş satırı panelindeki varyant combo'su) — varyant-başı fiyatı (ana varyant
    /// öncelikli, sonra koda göre) ile. Tek varyantlıysa panel VariantId'yi null bırakır; çokluysa fiyatı seçilen varyant belirler.</summary>
    Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid goodId);

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ).
    /// Jenerik agnostik sisteme (EntityVariantGraphService) delege eder. Client round-trip; kalıcılaşma Good save'inde.</summary>
    Task<List<GoodVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input);
}
