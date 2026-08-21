using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Products;
using Integration.TradeXpress.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.Services;

public partial class ServiceListPage
{
    public ServiceListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IServiceAppService ServiceAppService { get; set; } = default!;

    [Microsoft.AspNetCore.Components.Inject]
    protected IViewOpener ViewOpener { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        ServiceGetDto, ServiceListDto, Guid,
        ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto> CrudAppService
        => ServiceAppService;

    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Services.ServiceEditHost);

    // Tenant, global (host) Service'i silemez — UI tarafında da engelle (server zaten bloklar).
    public override async Task DeleteAsync()
    {
        var selectedItems = StateService.SelectedDataItems;
        if (selectedItems == null || selectedItems.Count == 0)
        {
            return;
        }

        if (CurrentTenant.Id != null)
        {
            var hasGlobal = selectedItems.OfType<ServiceListDto>().Any(x => x.IsGlobal);
            if (hasGlobal)
            {
                UiService.ShowWarningToast(L["TradeXpress:Service:CannotDeleteGlobalAsTenant"]);
                return;
            }
        }

        await base.DeleteAsync();
    }

    /// <summary>Toolbar'ın özel aksiyonu: "Ürün Oluştur". Her çizimde kurulur çünkü etkinliği SEÇİME
    /// bağlıdır.</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        var selected = StateService.SelectedDataItems?.OfType<ServiceListDto>().ToList() ?? new List<ServiceListDto>();

        return new List<CrudToolbarAction>
        {
            CommodityProductAction.Build(
                L,
                selected.Count,
                () => OpenProductFromServiceAsync(selected.Count == 1 ? selected[0].Id : Guid.Empty)),
        };
    }

    /// <summary>Hizmetin ürün projeksiyonunu ÜRÜN formunda açar (kayıt AÇILMAZ — seed).
    ///
    /// <para>Hizmet stoklanan emtia değil, reçeteye giren ÜCRET kalemidir; varyantı da görseli de yoktur.
    /// Ürün tek ana varyantla doğar. <b>Yalnız hizmetten oluşan ürünün stok politikası <c>Unlimited</c>
    /// olmalıdır</b> — <c>Calculated</c> yapmak stok zincirinin veri bulamayacağı bir hesap açar ve sonuç
    /// sessizce 0'a düşer; bu tercihi kullanıcı ürün formunda verir (seed onu ZORLAMAZ).</para></summary>
    private async Task OpenProductFromServiceAsync(Guid serviceId)
    {
        if (serviceId == Guid.Empty)
        {
            return;
        }

        try
        {
            var seed = await ServiceAppService.ProjectToProductAsync(serviceId);

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
