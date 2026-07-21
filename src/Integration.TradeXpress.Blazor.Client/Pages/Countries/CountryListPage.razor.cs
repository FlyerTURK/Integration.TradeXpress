using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Geography;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Pages.Countries;

public partial class CountryListPage
{
    public CountryListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected ICountryAppService CountryAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected IGeographyAppService GeographyAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    // Dostane hata çözümü (CrudErrorPresenter) için — CrudPageBase'inki private, kendi referansımız gerekir.
    [Microsoft.AspNetCore.Components.Inject]
    protected IServiceProvider PageServiceProvider { get; set; } = default!;

    /// <summary>Toolbar custom aksiyonu: "Coğrafyayı İçe Aktar" (OrderListPage "Siparişleri Çek" deseni).</summary>
    private IReadOnlyList<CrudToolbarAction>? _geographyActions;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        _geographyActions = BuildGeographyActions();
    }

    /// <summary>Varsayılan para birimi linki → o birimin edit'ini MDI sekmesinde aç (Id Code'dan çözüldü; yoksa no-op).</summary>
    private async Task OpenUnitAsync(Guid? unitId, string? code)
    {
        if (unitId is not { } id || id == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
    }

    // "Coğrafyayı İçe Aktar" — seçili ülkenin il/eyalet + şehir verisini lazy import tetiğiyle DB'ye indirir
    // (zaten doluysa server no-op; UI hep DB'den okur). SortIndex=300 = kataloğun custom slot'u.
    private IReadOnlyList<CrudToolbarAction> BuildGeographyActions()
    {
        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 300,
                Text = L["Geography:Import"],
                Tooltip = L["Geography:Import:Tooltip"],
                IconCssClass = TradeXpressIcons.Download + " xaf-toolbar-item-icon",
                OnClick = ImportGeographyAsync,
            },
        };
    }

    private async Task ImportGeographyAsync()
    {
        var selected = StateService.SelectedItem;
        if (selected == null)
        {
            UiService.ShowWarningToast(L["Geography:Import:SelectCountry"]);
            return;
        }

        try
        {
            StateService.IsBusy = true;
            StateService.NotifyStateChanged();

            // Lazy tetik: liste isteği veri yoksa importu başlatır; dönen sayı toast'ta gösterilir.
            var areas = await GeographyAppService.GetAdministrativeAreasAsync(selected.Id);
            UiService.ShowSuccessToast(string.Format(L["Geography:Import:Success"], selected.Name, areas.Items.Count));
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, PageServiceProvider) ?? ex.Message);
        }
        finally
        {
            StateService.IsBusy = false;
            StateService.NotifyStateChanged();
        }
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        CountryGetDto, CountryListDto, Guid,
        CountryListRequestDto, CountryCreateDto, CountryUpdateDto> CrudAppService
        => CountryAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Countries.Default;

    // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator (eski CountryEditPage repo'da kalır).
    public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Countries.CountryEditHost);

    // Edit TAB'da açılır (default: liste tab'da → edit tab'da). Faz 4 quick-add: döviz "+" formu POPUP açar;
    // tek popup host nested olmadığından host edit'in popup'ta OLMAMASI gerekir. (Popup örneği Vault pilotunda.)
}
