using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Goods;

namespace Integration.TradeXpress.Blazor.Client.Pages.Goods;

public partial class GoodListPage
{
    public GoodListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IGoodAppService GoodAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService Working { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Working.EnsureLoadedAsync();
        await base.OnInitializedAsync();
    }

    protected override void OnConfiguringListRequest(GoodListRequestDto request)
    {
        request.CompanyId = Working.CurrentCompanyId;
    }

    public override Volo.Abp.Application.Services.ICrudAppService<
        GoodGetDto, GoodListDto, Guid,
        GoodListRequestDto, GoodCreateDto, GoodUpdateDto> CrudAppService
        => GoodAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Goods.GoodEditHost);

    [Microsoft.AspNetCore.Components.Inject]
    protected Integration.Framework.Blazor.Client.Services.Base.IViewOpener ViewOpener { get; set; } = default!;

    /// <summary>
    /// "ÜRÜN OLUŞTUR" — seçili mamülün ürün aynasını açar (2026-08-10 Hakan: "mamül listeleme formunda
    /// toolbara"). <c>ProductCommodityClassificationPanel</c>'in TERS yönü; ikisi de aynı tohumlama
    /// mekanizmasını (<c>SeedModel</c>) kullanır.
    ///
    /// <para><b>Seçim gerektirir ama GİZLENMEZ, devre dışı kalır:</b> görünmeyen düğme "böyle bir şey yok"
    /// der, soluk düğme "bir mamül seç" der — ikincisi doğru bilgidir (gönderim geçmişi düğmesiyle aynı
    /// gerekçe).</para>
    ///
    /// <para><b>TEK kayıt:</b> çoklu seçimde hangi mamülden üretileceği belirsizdir; toplu üretim ayrı bir
    /// karardır ve sessizce ilkini seçmek kullanıcının görmediği bir tercih olurdu.</para>
    /// </summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<GoodListDto>().ToList() ?? new List<GoodListDto>();

        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 300,
                Text = L["Good:CreateProduct"],
                Tooltip = L["Good:CreateProductTooltip"],
                IconCssClass = TradeXpressIcons.Product + " xaf-toolbar-item-icon",
                Enabled = selected.Count == 1,
                OnClick = () => OpenProductFromGoodAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty),
            },
        };
    }

    /// <summary>Mamülün ürün aynasını ÜRÜN formunda açar. Projeksiyon SUNUCUDA üretilir (yetki kapısı orada)
    /// ve forma <c>SeedModel</c> olarak verilir — kayıt AÇILMAZ, kullanıcı ürüne özel alanları doldurup
    /// kendisi kaydeder.</summary>
    private async Task OpenProductFromGoodAsync(Guid goodId)
    {
        if (goodId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await GoodAppService.ProjectToProductAsync(goodId);

            await ViewOpener.OpenAsync(
                typeof(Integration.TradeXpress.Blazor.Client.Pages.Products.ProductEditHost),
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

    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<GoodListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Good:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }
}
