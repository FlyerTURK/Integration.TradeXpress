using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Metals;

namespace Integration.TradeXpress.Blazor.Client.Pages.Metals;

public partial class MetalListPage
{
    public MetalListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IMetalAppService MetalAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    /// <summary>Takip edilen para birimi linki → o birimin edit'ini MDI sekmesinde aç (yoksa no-op).</summary>
    private async Task OpenUnitAsync(Guid? unitId, string? code)
    {
        if (unitId is not { } id || id == Guid.Empty)
        {
            return;
        }

        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        MetalGetDto, MetalListDto, Guid,
        MetalListRequestDto, MetalCreateDto, MetalUpdateDto> CrudAppService
        => MetalAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Metals.MetalEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<MetalListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Metal:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    private System.Collections.Generic.IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction>? _customActions;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _customActions = new System.Collections.Generic.List<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction>
        {
            new Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction
            {
                SortIndex = 150,
                Text = L["MetalReport"].Value,
                AdaptiveText = L["MetalReport"].Value,
                Tooltip = L["MetalReport"].Value,
                IconCssClass = "custom-icon-report",
                OnClick = async () => await TabManager.OpenOrActivateAsync("/reports/metal", L["MetalReport"].Value, "custom-icon-report")
            }
        };
    }
}

