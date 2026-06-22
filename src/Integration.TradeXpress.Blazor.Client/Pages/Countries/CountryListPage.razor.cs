using System;
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

    public override Volo.Abp.Application.Services.ICrudAppService<
        CountryGetDto, CountryListDto, Guid,
        CountryListRequestDto, CountryCreateDto, CountryUpdateDto> CrudAppService
        => CountryAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Countries.Default;

        // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator (eski CountryEditPage repo'da kalır).
        public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Countries.CountryEditHost);

        // OPSİYONEL POPUP override (örnek): otomatik default tab olsa da bu liste edit'i POPUP açar.
        // Aynı CountryEditHost hem tab hem popup açılabilir (popup'ta IPopupChrome, tab'da CurrentMdiTab).
        protected override Integration.Framework.Blazor.Client.Components.Crud.EditOpenTarget EditOpenTarget
            => Integration.Framework.Blazor.Client.Components.Crud.EditOpenTarget.Popup;
    }


