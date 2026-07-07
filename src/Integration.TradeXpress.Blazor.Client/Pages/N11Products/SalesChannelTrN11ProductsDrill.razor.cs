using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Bir N11 kanalına AİT ürün listelemeleri (KANAL-merkezli) — persistent drill. Kanal edit formunun içinde
/// açılır: kanaldaki N11 listelemelerini gösterir, düzenler, N11'e gönderir (push), yerel siler. YENİ listeleme
/// ürün tarafından yapılır (fiyat/stok/görsel orada girildiğinden) → burada AllowAdd kapalı.</summary>
public partial class SalesChannelTrN11ProductsDrill : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelTrN11GetDto Channel { get; set; } = default!;

    [Inject] private ISalesChannelTrN11ProductAppService AppService { get; set; } = default!;
    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelTrN11ProductDto>? _drill;
    private List<SalesChannelTrN11ProductDto> _channelProducts = new();

    // Listeleme satırının ait olduğu ERP ürününün kimlik alanları (grid'de kod/ad göstermek için; DTO'da yok).
    private Dictionary<Guid, ProductListDto> _products = new();

    // Edit formundaki kanal göstergesi (read-only) için tek-öğeli kanal listesi.
    private List<SalesChannelListDto> _channelAsList = new();

    protected override async Task OnInitializedAsync()
    {
        _channelAsList = new List<SalesChannelListDto>
        {
            new()
            {
                Id = Channel.Id,
                Code = Channel.Code,
                Name = Channel.Name,
                ChannelType = SalesChannelType.TrN11,
                IsActive = Channel.IsActive,
            },
        };

        var products = await ProductAppService.GetListAsync(new ProductListRequestDto { MaxResultCount = 1000 });
        _products = products.Items.ToDictionary(p => p.Id);

        await ReloadChannelProductsAsync();
    }

    private async Task ReloadChannelProductsAsync()
    {
        _channelProducts = await AppService.GetListForChannelAsync(Channel.Id);
    }

    // Elle eklenmez (AllowAdd=false) ama DrillList NewItemFactory ister — trivial (UI'dan çağrılmaz).
    private SalesChannelTrN11ProductDto NewChannelProduct()
    {
        return new SalesChannelTrN11ProductDto { SalesChannelId = Channel.Id };
    }

    private SalesChannelTrN11ProductDto CloneChannelProduct(SalesChannelTrN11ProductDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<SalesChannelTrN11ProductDto>(json)!;
    }

    private async Task<SalesChannelTrN11ProductDto> PersistUpdate(SalesChannelTrN11ProductDto channelProduct)
    {
        var input = Mapper.Map<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductUpdateDto>(channelProduct);
        return await AppService.UpdateAsync(channelProduct.Id, input);
    }

    private async Task PersistDelete(SalesChannelTrN11ProductDto channelProduct)
    {
        await AppService.DeleteAsync(channelProduct.Id);
    }

    // Satır push: listelemeyi N11'e gönder (SaveProduct); durum güncellensin diye listeyi tazele.
    private async Task PushAsync(SalesChannelTrN11ProductDto channelProduct)
    {
        try
        {
            var pushed = await AppService.PushToN11Async(channelProduct.Id);
            await ReloadChannelProductsAsync();
            UiService.ShowSuccessToast(L["N11Product:PushSuccess"].Value);

            // Eşitleme uyarıları (ör. N11 kategoriyi değiştirdi) — güvenli bilgilendirme (2026-07-07 kararı).
            foreach (var warning in pushed.SyncWarnings)
            {
                UiService.ShowWarningToast(warning);
            }

            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private string ProductCodeOf(SalesChannelTrN11ProductDto channelProduct)
    {
        return _products.TryGetValue(channelProduct.ProductId, out var p) ? p.Code : string.Empty;
    }

    private string ProductNameOf(SalesChannelTrN11ProductDto channelProduct)
    {
        return _products.TryGetValue(channelProduct.ProductId, out var p) ? p.Name : string.Empty;
    }

    // Grid'de ürün etiketi: "KOD — Ad" (ad boşsa yalnız kod).
    private string ProductLabelOf(SalesChannelTrN11ProductDto channelProduct)
    {
        var name = ProductNameOf(channelProduct);
        var code = ProductCodeOf(channelProduct);
        return string.IsNullOrEmpty(name) ? code : $"{code} — {name}";
    }

    // N11'e gönderilmediyse "Gönderilmedi", gönderildiyse "SaleStatus / ApprovalStatus".
    private string StatusTextOf(SalesChannelTrN11ProductDto channelProduct)
    {
        if (!channelProduct.N11ProductId.HasValue)
        {
            return L["N11Product:NotSent"].Value;
        }

        return $"{channelProduct.SaleStatus} / {channelProduct.ApprovalStatus}";
    }
}
