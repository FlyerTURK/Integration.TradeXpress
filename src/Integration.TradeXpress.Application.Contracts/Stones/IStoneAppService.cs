using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Stones;

public interface IStoneAppService : ICrudAppService<
    StoneGetDto,
    StoneListDto,
    Guid,
    StoneListRequestDto,
    StoneCreateDto,
    StoneUpdateDto>
{
    /// <summary>Taş süreç paneli combo'su için host + çalışılan şirkete-özel kayıtlar (koda göre sıralı).</summary>
    Task<List<StoneListDto>> GetPickerListAsync(Guid? companyId = null);

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ; jenerik servise delege).</summary>
    Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input);

    /// <summary>Bir taşın AKTİF varyantları (fiş satırı panelindeki varyant combo'su) — fiyatsız (fiyat taş seviyesinde).</summary>
    Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid stoneId);
}
