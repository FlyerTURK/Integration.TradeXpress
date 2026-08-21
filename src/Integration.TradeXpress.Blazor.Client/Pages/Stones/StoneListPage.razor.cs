using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Stones;

namespace Integration.TradeXpress.Blazor.Client.Pages.Stones;

public partial class StoneListPage
{
    public StoneListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IStoneAppService StoneAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService Working { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await base.OnInitializedAsync();
    }

    // Liste isteğine çalışılan şirketi koy → server host(null) + bu şirkete-özel taşları döner.
    protected override void OnConfiguringListRequest(StoneListRequestDto request)
    {
        request.CompanyId = Working.CurrentCompanyId;
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        StoneGetDto, StoneListDto, Guid,
        StoneListRequestDto, StoneCreateDto, StoneUpdateDto> CrudAppService
        => StoneAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Stones.StoneEditHost);

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<StoneListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Stone:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    /// <summary>Toolbar'ın özel aksiyonu: "Ürün Oluştur". Her çizimde kurulur çünkü etkinliği SEÇİME
    /// bağlıdır.</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<StoneListDto>().ToList() ?? new List<StoneListDto>();

        return new List<CrudToolbarAction>
        {
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromStoneAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Taşın ürün projeksiyonunu ÜRÜN formunda açar (kayıt AÇILMAZ — seed).
    ///
    /// <para>Taş VARYANTSIZDIR ("her taşın parmak izi ayrıdır", 2026-08-09) ama kayıt-geneli MEDYA taşır —
    /// ikisi ayrı sorulardır. Ürün tek ana varyantla doğar ve o varyantın kodu taşın kodudur.</para></summary>
    private async Task OpenProductFromStoneAsync(Guid stoneId)
    {
        if (stoneId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await StoneAppService.ProjectToProductAsync(stoneId);

            await ViewOpener.OpenAsync(
                typeof(ProductEditHost),
                null,
                L["Product"].Value,
                iconCssClass: null,
                extraParams: new Dictionary<string, object> { ["SeedModel"] = seed });
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
    }
}
