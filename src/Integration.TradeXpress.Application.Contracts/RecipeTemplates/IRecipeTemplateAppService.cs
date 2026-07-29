using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.RecipeTemplates;

public interface IRecipeTemplateAppService : ICrudAppService<
    RecipeTemplateGetDto,
    RecipeTemplateListDto,
    Guid,
    RecipeTemplateListRequestDto,
    RecipeTemplateCreateDto,
    RecipeTemplateUpdateDto>
{
    /// <summary>Combo/picker — aktif şablonlar, DisplayOrder+Ad sıralı.</summary>
    Task<List<RecipeTemplateListDto>> GetPickerListAsync();

    /// <summary>
    /// Şablonu bir ürünün TÜM varyantlarına uygular. Muadillikten gelen ve kullanıcının elle girdiği satırlara
    /// DOKUNMAZ; kendi satırlarını onların ardına ekler. Yeniden uygulanırsa yalnız önceki şablon satırları
    /// tazelenir (idempotent — satırlar katlanmaz).
    /// </summary>
    Task<RecipeTemplateApplyResultDto> ApplyToProductAsync(Guid templateId, Guid productId);
}
