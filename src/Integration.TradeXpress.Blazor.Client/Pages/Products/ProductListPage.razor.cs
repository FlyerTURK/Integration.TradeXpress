using System;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

public partial class ProductListPage
{
    public ProductListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Microsoft.AspNetCore.Components.Inject]
    protected IProductAppService ProductAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        ProductGetDto, ProductListDto, Guid,
        ProductListRequestDto, ProductCreateDto, ProductUpdateDto> CrudAppService
        => ProductAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Products.Default;

    // Agnostic CrudEditHost + PersistentCoordinator (edit TAB'da açılır) — AssayOffice deseni.
    public override System.Type EditComponentType
        => typeof(Integration.TradeXpress.Blazor.Client.Pages.Products.ProductEditHost);
}
