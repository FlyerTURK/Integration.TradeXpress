using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.TrendyolProducts;

/// <summary>Ürünün TRENDYOL kanal ürünleri — IN-MEMORY GRAF drill (N11 <c>SalesChannelProductsPanel</c>'in AYRI ikinci
/// kopyası; eleştiri F1: iki ayrı liste, N11 paneline dokunmadan additive). Ürün grafının parçası; ürün 'Kaydet'inde
/// birlikte kaydedilir (ürün önceden kaydedilmese de eklenebilir). "Yeni" SPLIT buton: kanal OTOMATİK (şirkette TEK
/// Trendyol kanalı) + SET-ONCE. Push (batch-async) + Durum Yenile yalnız KAYDEDİLMİŞ (Id'li) satırda.</summary>
public partial class SalesChannelTrendyolProductsPanel : CrudComponentBase
{
    /// <summary>Ürün grafındaki Trendyol kanal ürünleri (Model.SalesChannelTrendyolProducts) — in-memory düzenlenir.</summary>
    [Parameter, EditorRequired] public List<SalesChannelTrTrendyolProductDto> Items { get; set; } = default!;

    /// <summary>Bağlı ürünün Id'si (kaydedilmişse dolu; yeni üründe Guid.Empty). Push + create için.</summary>
    [Parameter] public Guid ProductId { get; set; }

    /// <summary>Bağlı ürünün canlı grafı (ProductLayout.Model) — yeni Trendyol taslağının ürün-genel varsayılanlarını
    /// (Description/DeliveryDuration...) create-copy ile devralması için. Panel yalnız OKUR (dumb); mutate ETMEZ.</summary>
    [Parameter] public ProductGetDto? ProductDefaults { get; set; }

    /// <summary>Graf değişti — parent (ProductLayout) EditChanged'i tetikler (Save aktifliği).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private ISalesChannelTrTrendyolProductAppService AppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelTrTrendyolProductDto>? _drill;
    private List<SalesChannelListDto> _channels = new();

    protected override async Task OnInitializedAsync()
    {
        // Trendyol kanalları (kanal adı çözümü + yeni kayıtta otomatik kanal ataması; şirkette TEK Trendyol kanalı kuralı).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrTrendyol).ToList();
    }

    // ── "Yeni" SPLIT buton: ana tık = Trendyol (tek tip); ▾ alt menüden tip. Built-in Yeni kapalı (AllowAdd=false). ──
    private IReadOnlyList<CrudToolbarAction> PanelActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 0,
            Text = L["New"],
            Tooltip = L["New"],
            IconCssClass = FrameworkIcons.Add,
            SplitDropDownButton = true,
            OnClick = StartNewTrendyolAsync,
            Items = new[]
            {
                new CrudToolbarAction
                {
                    Text = L["SalesChannelTrTrendyolProduct"],
                    IconCssClass = TradeXpressIcons.SalesChannel,
                    OnClick = StartNewTrendyolAsync,
                },
            },
        },
    };

    /// <summary>Yeni Trendyol kanal ürünü: şirketin TEK Trendyol kanalını otomatik bul (yoksa dostane uyarı) →
    /// kanal atanmış taslakla popup'ı aç. Kanal kullanıcıya SORULMAZ + sonradan değiştirilemez (set-once).</summary>
    private Task StartNewTrendyolAsync()
    {
        var channel = _channels.FirstOrDefault();
        if (channel is null)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:ChannelMissing"].Value);
            return Task.CompletedTask;
        }

        _drill?.StartNewItem(BuildNewTrendyolDraft(channel.Id));
        return Task.CompletedTask;
    }

    /// <summary>Trendyol kanal ürünü taslağı — TEK üretim yeri (split akışı + NewItemFactory aynı default'ları alır).
    /// Id boş (yeni graf düğümü); ClientKey DTO ctor'unda üretilir. Kanal-özel alanlara (Category/Brand/Attributes)
    /// dokunulmaz — kullanıcı Trendyol'da girer.</summary>
    private SalesChannelTrTrendyolProductDto BuildNewTrendyolDraft(Guid salesChannelId)
    {
        var draft = new SalesChannelTrTrendyolProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            VatRate = 20,
            IsActive = true,
        };

        // Ürün-genel varsayılanlardan create-copy: form açılınca alanlar DOLU gelir, kullanıcı düzenler.
        if (ProductDefaults is { } p)
        {
            draft.Description = p.Description;
            draft.DeliveryDuration = p.PreparingDay;
        }

        return draft;
    }

    private SalesChannelTrTrendyolProductDto NewChannelProduct()
    {
        return BuildNewTrendyolDraft(_channels.FirstOrDefault()?.Id ?? Guid.Empty);
    }

    // Cancel geri alabilsin diye JSON deep-copy (attribute + varyant override listeleri dahil).
    private SalesChannelTrTrendyolProductDto CloneChannelProduct(SalesChannelTrTrendyolProductDto source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<SalesChannelTrTrendyolProductDto>(json)!;
    }

    // Satır push: yalnız KAYDEDİLMİŞ (Id'li) satırda. Yeni satır (Id boş) → önce ürünü kaydet uyarısı.
    private async Task PushAsync(SalesChannelTrTrendyolProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var pushed = await AppService.PushToTrendyolAsync(channelProduct.Id);
            CopyStatusInto(channelProduct, pushed);
            UiService.ShowSuccessToast(L["TrendyolProduct:PushSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Batch durum yenileme: yalnız gönderilmiş (BatchRequestId dolu) satırda.
    private async Task RefreshStatusAsync(SalesChannelTrTrendyolProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["TrendyolProduct:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var refreshed = await AppService.RefreshStatusAsync(channelProduct.Id);
            CopyStatusInto(channelProduct, refreshed);
            UiService.ShowSuccessToast(L["TrendyolProduct:RefreshSuccess"].Value);
            StateHasChanged();
        }
        catch (Exception ex)
        {
            UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
        }
    }

    // Push/refresh sonrası Trendyol durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf).
    private static void CopyStatusInto(SalesChannelTrTrendyolProductDto target, SalesChannelTrTrendyolProductDto source)
    {
        target.BatchRequestId = source.BatchRequestId;
        target.LastBatchRequestType = source.LastBatchRequestType;
        target.Status = source.Status;
        target.FailedItemCount = source.FailedItemCount;
        target.LastSyncedAt = source.LastSyncedAt;
        target.LastError = source.LastError;
        target.Skus = source.Skus;
    }

    private string ChannelCodeOf(SalesChannelTrTrendyolProductDto channelProduct)
    {
        return _channels.FirstOrDefault(c => c.Id == channelProduct.SalesChannelId)?.Code ?? string.Empty;
    }

    // Kaydedilmemiş (Id boş) → "Kaydedilmedi"; gönderilmemiş → "Gönderilmedi"; gönderildiyse batch durumu.
    private string StatusTextOf(SalesChannelTrTrendyolProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            return L["TrendyolProduct:NotSaved"].Value;
        }

        if (string.IsNullOrEmpty(channelProduct.BatchRequestId))
        {
            return L["TrendyolProduct:NotSent"].Value;
        }

        return channelProduct.Status ?? string.Empty;
    }
}
