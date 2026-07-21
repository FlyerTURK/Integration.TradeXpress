using System;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Shipments;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

public partial class ShipmentTemplateListPage
{
    public ShipmentTemplateListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        ShipmentTemplateGetDto, ShipmentTemplateListDto, Guid,
        ShipmentTemplateListRequestDto, ShipmentTemplateCreateDto, ShipmentTemplateUpdateDto> CrudAppService
        => ShipmentTemplateAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.ShipmentTemplates.Default;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Shipments.ShipmentTemplateEditHost);
}
