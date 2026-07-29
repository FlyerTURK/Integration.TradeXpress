using System;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.RecipeTemplates;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.RecipeTemplates;

public partial class RecipeTemplateListPage
{
    public RecipeTemplateListPage()
    {
        LocalizationResource = typeof(Localization.TradeXpressResource);
    }

    [Inject] protected IRecipeTemplateAppService RecipeTemplateAppService { get; set; } = default!;

    public override ICrudAppService<
        RecipeTemplateGetDto, RecipeTemplateListDto, Guid,
        RecipeTemplateListRequestDto, RecipeTemplateCreateDto, RecipeTemplateUpdateDto> CrudAppService
    {
        get { return RecipeTemplateAppService; }
    }

    protected override string PermissionPrefix
    {
        get { return TradeXpressPermissions.RecipeTemplates.Default; }
    }

    public override Type EditComponentType
    {
        get { return typeof(RecipeTemplateEditHost); }
    }
}
