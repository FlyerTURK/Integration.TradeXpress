using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Products;

/// <summary>Product edit host code-behind — coordinator kurulumu + yeni-kayıt varsayılanları. Varyant ana
/// değişmezi sunucuda (ProductVariantManager); yeni ürün varyantsız açılır, kaydedince main otomatik doğar.</summary>
public partial class ProductEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IProductAppService ProductAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto>? _coordinator;
    private bool _ready;

    protected override Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<ProductGetDto, ProductListDto, Guid, ProductListRequestDto, ProductCreateDto, ProductUpdateDto>(
            ProductAppService, Mapper);
        _ready = true;
        return Task.CompletedTask;
    }

    // Yeni kayıt: aktif. Varyant seed EDİLMEZ: sunucu ProductVariantManager en-az-1 + main garantisini
    // ürün kimliğinden kurar.
    private void ApplyNew(ProductGetDto m)
    {
        m.IsActive = true;
    }
}
