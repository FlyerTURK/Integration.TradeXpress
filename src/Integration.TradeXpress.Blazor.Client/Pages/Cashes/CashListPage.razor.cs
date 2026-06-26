using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Cashes;

public partial class CashListPage
{
    public CashListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected ICashAppService CashAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    /// <summary>Takip edilen para birimi linki → o birimin edit'ini MDI sekmesinde aç (yoksa no-op).</summary>
    private async Task OpenUnitAsync(Guid? unitId, string? code)
    {
        if (unitId is not { } id || id == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        CashGetDto, CashListDto, Guid,
        CashListRequestDto, CashCreateDto, CashUpdateDto> CrudAppService
        => CashAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Cashes.Default;

    // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator (edit TAB'da açılır).
    public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Cashes.CashEditHost);

    // Tenant, global (host) Cash'i silemez — UI tarafında da engelle (server zaten bloklar).
    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<CashListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Cash:CannotDeleteGlobalAsTenant"]);
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
                Text = L["CashReport"].Value,
                AdaptiveText = L["CashReport"].Value,
                Tooltip = L["CashReport"].Value,
                IconCssClass = "custom-icon-report",
                OnClick = async () => await TabManager.OpenOrActivateAsync("/reports/cash", L["CashReport"].Value, "custom-icon-report")
            }
        };
    }
}

