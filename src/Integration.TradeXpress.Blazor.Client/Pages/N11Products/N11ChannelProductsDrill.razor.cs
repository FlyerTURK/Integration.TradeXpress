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
public partial class N11ChannelProductsDrill : CrudComponentBase
{
    [Parameter, EditorRequired] public SalesChannelTrN11GetDto Channel { get; set; } = default!;

    [Inject] private IN11ProductListingAppService AppService { get; set; } = default!;
    [Inject] private IProductAppService ProductAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<N11ProductListingDto>? _drill;
    private List<N11ProductListingDto> _listings = new();

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

        await ReloadListingsAsync();
    }

    private async Task ReloadListingsAsync()
    {
        _listings = await AppService.GetListForChannelAsync(Channel.Id);
    }

    // Elle eklenmez (AllowAdd=false) ama DrillList NewItemFactory ister — trivial (UI'dan çağrılmaz).
    private N11ProductListingDto NewListing()
    {
        return new N11ProductListingDto { SalesChannelId = Channel.Id };
    }

    private N11ProductListingDto CloneListing(N11ProductListingDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<N11ProductListingDto>(json)!;
    }

    private async Task<N11ProductListingDto> PersistUpdate(N11ProductListingDto listing)
    {
        var input = Mapper.Map<N11ProductListingDto, N11ProductListingUpdateDto>(listing);
        return await AppService.UpdateAsync(listing.Id, input);
    }

    private async Task PersistDelete(N11ProductListingDto listing)
    {
        await AppService.DeleteAsync(listing.Id);
    }

    // Satır push: listelemeyi N11'e gönder (SaveProduct); durum güncellensin diye listeyi tazele.
    private async Task PushAsync(N11ProductListingDto listing)
    {
        try
        {
            await AppService.ListToN11Async(listing.Id);
            await ReloadListingsAsync();
            UiService.ShowSuccessToast(L["N11Listing:PushSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private string ProductCodeOf(N11ProductListingDto listing)
    {
        return _products.TryGetValue(listing.ProductId, out var p) ? p.Code : string.Empty;
    }

    private string ProductNameOf(N11ProductListingDto listing)
    {
        return _products.TryGetValue(listing.ProductId, out var p) ? p.Name : string.Empty;
    }

    // Grid'de ürün etiketi: "KOD — Ad" (ad boşsa yalnız kod).
    private string ProductLabelOf(N11ProductListingDto listing)
    {
        var name = ProductNameOf(listing);
        var code = ProductCodeOf(listing);
        return string.IsNullOrEmpty(name) ? code : $"{code} — {name}";
    }

    // N11'e gönderilmediyse "Gönderilmedi", gönderildiyse "SaleStatus / ApprovalStatus".
    private string StatusTextOf(N11ProductListingDto listing)
    {
        if (!listing.N11ProductId.HasValue)
        {
            return L["N11Listing:NotSent"].Value;
        }

        return $"{listing.SaleStatus} / {listing.ApprovalStatus}";
    }
}
