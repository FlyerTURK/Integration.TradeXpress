using System;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.AddOns;

public partial class AddOnListPage
{
    public AddOnListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IAddOnAppService AddOnAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        AddOnGetDto, AddOnListDto, Guid,
        AddOnListRequestDto, AddOnCreateDto, AddOnUpdateDto> CrudAppService
        => AddOnAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.AddOns.Default;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.AddOns.AddOnEditHost);
}
