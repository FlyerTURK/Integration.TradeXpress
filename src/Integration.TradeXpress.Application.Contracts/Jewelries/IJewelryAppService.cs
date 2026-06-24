using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
}
