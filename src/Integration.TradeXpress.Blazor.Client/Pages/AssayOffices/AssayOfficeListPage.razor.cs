using System;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.AssayOffices;

public partial class AssayOfficeListPage
{
    public AssayOfficeListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IAssayOfficeAppService AssayOfficeAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        AssayOfficeGetDto, AssayOfficeListDto, Guid,
        AssayOfficeListRequestDto, AssayOfficeCreateDto, AssayOfficeUpdateDto> CrudAppService
        => AssayOfficeAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.AssayOffices.Default;

    // YENİ mimari: agnostic CrudEditHost + PersistentCoordinator (edit TAB'da açılır).
    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.AssayOffices.AssayOfficeEditHost);
}
