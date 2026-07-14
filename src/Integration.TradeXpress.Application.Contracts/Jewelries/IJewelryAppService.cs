using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Jewelries;

public interface IJewelryAppService : ICrudAppService<
    JewelryGetDto,
    JewelryListDto,
    Guid,
    JewelryListRequestDto,
    JewelryCreateDto,
    JewelryUpdateDto>
{
    /// <summary>Mücevher süreç paneli combo'su için host + çalışılan şirkete-özel kayıtlar (koda göre sıralı).</summary>
    Task<List<JewelryListDto>> GetPickerListAsync(Guid? companyId = null);

    /// <summary>Persistsiz varyant önizlemesi — nitelik×değer kartezyeni → varyant graf satırları (DB'ye YAZMAZ; jenerik servise delege).</summary>
    Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input);

    /// <summary>Bir mücevherin AKTİF varyantları (fiş satırı panelindeki varyant combo'su) — fiyatsız (fiyat mücevher seviyesinde).</summary>
    Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid jewelryId);
}
