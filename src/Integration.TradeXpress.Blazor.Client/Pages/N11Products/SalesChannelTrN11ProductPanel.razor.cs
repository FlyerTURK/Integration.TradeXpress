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
public partial class SalesChannelTrN11ProductPanel : CrudComponentBase
{
    [Parameter, EditorRequired] public Guid ProductId { get; set; }

    [Inject] private ISalesChannelTrN11ProductAppService AppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IObjectMapper Mapper { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelTrN11ProductDto>? _drill;
    private List<SalesChannelTrN11ProductDto> _channelProducts = new();
    private List<SalesChannelListDto> _channels = new();

    protected override async Task OnInitializedAsync()
    {
        // Yalnız N11 kanalları (create'te kanal seçici + mevcut listeleme taraması bunlardan).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();

        await ReloadChannelProductsAsync();
    }

    // Ürünün her N11 kanalındaki listelemesini toplar (yoksa atlar).
    private async Task ReloadChannelProductsAsync()
    {
        var result = new List<SalesChannelTrN11ProductDto>();
        foreach (var channel in _channels)
        {
            var channelProduct = await AppService.GetForProductAsync(ProductId, channel.Id);
            if (channelProduct != null)
            {
                result.Add(channelProduct);
            }
        }

        _channelProducts = result;
    }

    // Yeni listeleme: ürün sabit; kanal + kategori edit formunda seçilir. Varsayılanlar N11 mandallarıyla.
    private SalesChannelTrN11ProductDto NewChannelProduct()
    {
        return new SalesChannelTrN11ProductDto
        {
            ProductId = ProductId,
            Condition = N11ProductCondition.New,
            Domestic = true,
            PreparingDay = 1,
            IsActive = true,
        };
    }

    // Cancel geri alabilsin diye JSON deep-copy (attribute + özel bilgi listeleri dahil).
    private SalesChannelTrN11ProductDto CloneChannelProduct(SalesChannelTrN11ProductDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<SalesChannelTrN11ProductDto>(json)!;
    }

    private async Task<SalesChannelTrN11ProductDto> PersistCreate(SalesChannelTrN11ProductDto channelProduct)
    {
        var input = Mapper.Map<SalesChannelTrN11ProductDto, SalesChannelTrN11ProductCreateDto>(channelProduct);
        input.ProductId = ProductId;
        input.SalesChannelId = channelProduct.SalesChannelId;
        return await AppService.CreateAsync(input);
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
            await AppService.PushToN11Async(channelProduct.Id);
            await ReloadChannelProductsAsync();
            UiService.ShowSuccessToast(L["N11Product:PushSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            // BusinessException (ImagesRequired/NoPricedVariant...) in-process lokalize olmaz → kodu çevir.
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    private string ChannelCodeOf(SalesChannelTrN11ProductDto channelProduct)
    {
        return _channels.FirstOrDefault(c => c.Id == channelProduct.SalesChannelId)?.Code ?? string.Empty;
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

    // Grid enum kolonu için lokalize metin (ComboBoxEnumEdit ile aynı "Enum:{Tip}:{Değer}" anahtar formatı).
    private string EnumText(string enumTypeName, Enum value)
    {
        return L[$"Enum:{enumTypeName}:{value}"].Value;
    }
}
