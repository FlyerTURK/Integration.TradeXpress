using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.AddOns;

public interface IAddOnAppService : ICrudAppService<
    AddOnGetDto,
    AddOnListDto,
    System.Guid,
    AddOnListRequestDto,
    AddOnCreateDto,
    AddOnUpdateDto>
{
    /// <summary>Combo/picker (ürün "Seçenekler" ataması) — aktif eklentiler, DisplayOrder+Name sıralı.</summary>
    Task<List<AddOnListDto>> GetPickerListAsync();
}
