using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Ürünün SATIŞ KANALI ÜRÜNLERİ — PERSISTENT drill (ürün edit formunun içinde). "Yeni" SPLIT buton:
/// kanal-ürün tipi alt menüden seçilir (şimdilik N11; Trendyol UI'sı gelince buraya eklenir). N11 seçilince kanal
/// OTOMATİK çözülür (şirkette TEK N11 kanalı kuralı) — kanal seçici yok, kanal SET-ONCE. Aynı kanalda aynı ürün
/// için birden fazla kayıt olabilir (2026-07-07). Satır başına "N11'e Gönder" (SaveProduct push).</summary>
public partial class SalesChannelProductsPanel : CrudComponentBase
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
        // N11 kanalları (kanal adı çözümü + yeni kayıtta otomatik kanal ataması; şirkette TEK N11 kanalı kuralı).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();

        await ReloadChannelProductsAsync();
    }

    // Ürünün TÜM kanal ürünleri — tek sorgu (aynı kanalda çok kayıt olabilir).
    private async Task ReloadChannelProductsAsync()
    {
        _channelProducts = await AppService.GetListForProductAsync(ProductId);
    }

    // ── "Yeni" SPLIT buton: ana tık = N11 (tek tip); ▾ alt menüden tip seçilir. Built-in Yeni kapalı (AllowAdd=false). ──
    private IReadOnlyList<CrudToolbarAction> PanelActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 0,
            Text = L["New"],
            Tooltip = L["New"],
            IconCssClass = FrameworkIcons.Add,
            SplitDropDownButton = true,
            OnClick = StartNewN11Async,
            Items = new[]
            {
                new CrudToolbarAction
                {
                    Text = L["SalesChannelTrN11Product"],
                    IconCssClass = TradeXpressIcons.SalesChannel,
                    OnClick = StartNewN11Async,
                },
            },
        },
    };

    /// <summary>Yeni N11 kanal ürünü: şirketin TEK N11 kanalını otomatik bul (yoksa dostane uyarı) →
    /// kanal atanmış taslakla popup'ı aç. Kanal kullanıcıya SORULMAZ + sonradan değiştirilemez (set-once).</summary>
    private Task StartNewN11Async()
    {
        var channel = _channels.FirstOrDefault();
        if (channel is null)
        {
            UiService.ShowWarningToast(L["N11Product:ChannelMissing"].Value);
            return Task.CompletedTask;
        }

        _drill?.StartNewItem(BuildNewN11Draft(channel.Id));
        return Task.CompletedTask;
    }

    /// <summary>N11 kanal ürünü taslağı — TEK üretim yeri (split akışı + NewItemFactory aynı default'ları alır).</summary>
    private SalesChannelTrN11ProductDto BuildNewN11Draft(Guid salesChannelId)
    {
        return new SalesChannelTrN11ProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            Condition = N11ProductCondition.New,
            Domestic = true,
            PreparingDay = 1,
            IsActive = true,
        };
    }

    // DrillList NewItemFactory zorunlu parametre — AllowAdd=false + split akışında KULLANILMAZ (StartNewItem ile
    // açılır); yine de erişilir olursa geçerli taslak üretsin (kanal yoksa Guid.Empty → sunucu ChannelNotFound verir).
    private SalesChannelTrN11ProductDto NewChannelProduct()
    {
        return BuildNewN11Draft(_channels.FirstOrDefault()?.Id ?? Guid.Empty);
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
