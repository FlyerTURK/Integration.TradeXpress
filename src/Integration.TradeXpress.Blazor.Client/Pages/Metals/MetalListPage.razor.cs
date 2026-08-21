using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
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

    [Microsoft.AspNetCore.Components.Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

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

    /// <summary>
    /// Toolbar'ın özel aksiyonları: maden raporu + "Ürün Oluştur".
    ///
    /// <para><b>Neden artık her çizimde kuruluyor</b> (eskiden <c>OnInitialized</c>'da BİR KEZ): "Ürün
    /// Oluştur" düğmesinin etkinliği SEÇİME bağlıdır ve seçim değiştiğinde yeniden hesaplanmalıdır. Bir kez
    /// kurulan liste, seçim yapılsa bile soluk kalırdı. Rapor aksiyonu aynen korundu — yalnız kurulduğu an
    /// değişti.</para>
    /// </summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<MetalListDto>().ToList() ?? new List<MetalListDto>();

        return new List<CrudToolbarAction>
        {
            new CrudToolbarAction
            {
                SortIndex = 150,
                Text = L["MetalReport"].Value,
                AdaptiveText = L["MetalReport"].Value,
                Tooltip = L["MetalReport"].Value,
                IconCssClass = "custom-icon-report",
                OnClick = async () => await TabManager.OpenOrActivateAsync("/reports/metal", L["MetalReport"].Value, "custom-icon-report"),
            },
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromMetalAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Madenin ürün projeksiyonunu ÜRÜN formunda açar. Projeksiyon SUNUCUDA üretilir ([Authorize] orada)
    /// ve forma <c>SeedModel</c> olarak verilir — kayıt AÇILMAZ, kullanıcı ürüne özel alanları (kategori,
    /// reçete, fiyat) doldurup kendisi kaydeder.</summary>
    private async Task OpenProductFromMetalAsync(Guid metalId)
    {
        if (metalId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await MetalAppService.ProjectToProductAsync(metalId);

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
