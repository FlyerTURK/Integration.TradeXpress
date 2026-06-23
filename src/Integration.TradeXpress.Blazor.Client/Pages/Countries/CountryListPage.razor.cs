using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Countries;
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
    protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    /// <summary>Varsayılan para birimi linki → o birimin edit'ini MDI sekmesinde aç (Id Code'dan çözüldü; yoksa no-op).</summary>
    private async Task OpenUnitAsync(Guid? unitId, string? code)
    {
        if (unitId is not { } id || id == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/currencies/currency-units/{id}",
            $"{L["CurrencyUnit"]}: {code}",
            TradeXpressIcons.CurrencyUnit);
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


