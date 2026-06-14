using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Currencies;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Blazor.Client.Pages.Currencies.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Currencies;

public partial class CurrencyUnitListPage
{
    public CurrencyUnitListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    /// <summary>"Margin Ayarla" action'ının açtığı yeniden kullanılabilir diyalog.</summary>
    protected MarginSetDialog? MarginDialog;

    [Inject]
    protected ICurrencyUnitAppService CurrencyUnitAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        CurrencyUnitGetDto, CurrencyUnitListDto, Guid,
        CurrencyUnitListRequestDto, CurrencyUnitCreateDto, CurrencyUnitUpdateDto> CrudAppService
        => CurrencyUnitAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.CurrencyUnits.Default;

    public override async Task BeforeUpdateAsync(CurrencyUnitListDto entity)
    {
        if (entity.IsGlobal && CurrentTenant.Id != null)
        {
            UiService.ShowWarningToast(L["TradeXpress:CurrencyUnit:CannotEditGlobalAsTenant"]);
            return;
        }
        await base.BeforeUpdateAsync(entity);
    }

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<CurrencyUnitListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:CurrencyUnit:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
