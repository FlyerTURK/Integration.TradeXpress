using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.VariantTemplates;

public interface IVariantTemplateAppService : ICrudAppService<
    VariantTemplateGetDto,
    VariantTemplateListDto,
    System.Guid,
    VariantTemplateListRequestDto,
    VariantTemplateCreateDto,
    VariantTemplateUpdateDto>
{
    /// <summary>Combo/picker ("Katalogtan Uygula") — aktif şablonlar, DisplayOrder+Name sıralı.</summary>
    Task<List<VariantTemplateListDto>> GetPickerListAsync();
}
