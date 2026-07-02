using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.AssayOffices;

public interface IAssayOfficeAppService : ICrudAppService<
    AssayOfficeGetDto,
    AssayOfficeListDto,
    System.Guid,
    AssayOfficeListRequestDto,
    AssayOfficeCreateDto,
    AssayOfficeUpdateDto>
{
    /// <summary>Combo/picker (takoz AyarEvi) — aktif ayar evleri, DisplayOrder+Name sıralı.</summary>
    Task<List<AssayOfficeListDto>> GetPickerListAsync();
}
