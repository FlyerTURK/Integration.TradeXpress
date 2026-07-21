using System;
using Integration.TradeXpress.VariantTemplates;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.VariantTemplates;

public partial class VariantTemplateListPage
{
    public VariantTemplateListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IVariantTemplateAppService VariantTemplateAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        VariantTemplateGetDto, VariantTemplateListDto, Guid,
        VariantTemplateListRequestDto, VariantTemplateCreateDto, VariantTemplateUpdateDto> CrudAppService
        => VariantTemplateAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.VariantTemplates.Default;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.VariantTemplates.VariantTemplateEditHost);
}
