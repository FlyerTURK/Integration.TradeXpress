using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Ürüne bağlı N11 listelemeleri (kanal başına bir) — PERSISTENT drill. N11 kanallarını + mevcut
/// listelemeleri yükler; CRUD anında AppService'e yazılır. Satır başına "N11'e Gönder" (SaveProduct push,
/// durumu tazeler). Ürün KAYDEDİLDİKTEN sonra (Id'li) açılır — yeni üründe gizli.</summary>
public partial class N11ProductListingPanel : CrudComponentBase
{
    [Parameter, EditorRequired] public Guid ProductId { get; set; }

    [Inject] private IN11ProductListingAppService AppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<N11ProductListingDto>? _drill;
    private List<N11ProductListingDto> _listings = new();
    private List<SalesChannelListDto> _channels = new();

    protected override async Task OnInitializedAsync()
    {
        // Yalnız N11 kanalları (create'te kanal seçici + mevcut listeleme taraması bunlardan).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();

        await ReloadListingsAsync();
    }

    // Ürünün her N11 kanalındaki listelemesini toplar (yoksa atlar).
    private async Task ReloadListingsAsync()
    {
        var result = new List<N11ProductListingDto>();
        foreach (var channel in _channels)
        {
            var listing = await AppService.GetForProductAsync(ProductId, channel.Id);
            if (listing != null)
            {
                result.Add(listing);
            }
        }

        _listings = result;
    }

    // Yeni listeleme: ürün sabit; kanal + kategori edit formunda seçilir. Varsayılanlar N11 mandallarıyla.
    private N11ProductListingDto NewListing()
    {
        return new N11ProductListingDto
        {
            ProductId = ProductId,
            Condition = N11ProductCondition.New,
            Domestic = true,
            PreparingDay = 1,
            IsActive = true,
        };
    }

    // Cancel geri alabilsin diye JSON deep-copy (attribute + özel bilgi listeleri dahil).
    private N11ProductListingDto CloneListing(N11ProductListingDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<N11ProductListingDto>(json)!;
    }

    private async Task<N11ProductListingDto> PersistCreate(N11ProductListingDto listing)
    {
        var input = Mapper.Map<N11ProductListingDto, N11ProductListingCreateDto>(listing);
        input.ProductId = ProductId;
        input.SalesChannelId = listing.SalesChannelId;
        return await AppService.CreateAsync(input);
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
            // BusinessException (ImagesRequired/NoPricedVariant...) in-process lokalize olmaz → kodu çevir.
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private string ChannelCodeOf(N11ProductListingDto listing)
    {
        return _channels.FirstOrDefault(c => c.Id == listing.SalesChannelId)?.Code ?? string.Empty;
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

    // Grid enum kolonu için lokalize metin (ComboBoxEnumEdit ile aynı "Enum:{Tip}:{Değer}" anahtar formatı).
    private string EnumText(string enumTypeName, Enum value)
    {
        return L[$"Enum:{enumTypeName}:{value}"].Value;
    }
}
