using System;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.ProductCategories;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.ProductCategories;

public partial class ProductCategoryListPage
{
    public ProductCategoryListPage()
    {
        LocalizationResource = typeof(Localization.TradeXpressResource);
    }

    [Inject] protected IProductCategoryAppService ProductCategoryAppService { get; set; } = default!;

    public override ICrudAppService<
        ProductCategoryGetDto, ProductCategoryListDto, Guid,
        ProductCategoryListRequestDto, ProductCategoryCreateDto, ProductCategoryUpdateDto> CrudAppService
    {
        get { return ProductCategoryAppService; }
    }

    protected override string PermissionPrefix
    {
        get { return TradeXpressPermissions.ProductCategories.Default; }
    }

    public override Type EditComponentType
    {
        get { return typeof(ProductCategoryEditHost); }
    }
}
