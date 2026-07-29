using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Products;

/// <summary>Ürünün SATIŞ KANALI ÜRÜNLERİ — IN-MEMORY GRAF drill (ürün grafının parçası; ürün 'Kaydet'inde birlikte
/// kaydedilir, ürün önceden kaydedilmese de eklenebilir — 2026-07-08 kullanıcı kararı). "Yeni" SPLIT buton: tip alt
/// menüden (şimdilik N11). Kanal OTOMATİK (şirkette TEK N11 kanalı) + SET-ONCE. Push/Stok-Fiyat yalnız KAYDEDİLMİŞ
/// (Id'li) satırda (yeni satır önce ürünle kaydedilmeli). Attribute/özel bilgi listeleri graf düğümünde taşınır.</summary>
public partial class SalesChannelProductsPanel : CrudComponentBase
{
    /// <summary>Ürün grafındaki N11 kanal ürünleri (Model.SalesChannelProducts) — in-memory düzenlenir.</summary>
    [Parameter, EditorRequired] public List<SalesChannelTrN11ProductDto> Items { get; set; } = default!;

    /// <summary>Bağlı ürünün Id'si (kaydedilmişse dolu; yeni üründe Guid.Empty). Push/sync + create için.</summary>
    [Parameter] public Guid ProductId { get; set; }

    /// <summary>Bağlı ürünün canlı grafı (ProductLayout.Model) — yeni N11 taslağının ürün-genel varsayılanlarını
    /// (Domestic/Condition/PreparingDay/... özel bilgi) create-copy ile devralması için. Panel yalnız OKUR (dumb);
    /// mutate ETMEZ. Boşsa (henüz bağlanmamış) sade default'lar kullanılır. Push'ta zaten server fallback var —
    /// bu UI kolaylığı (form açılınca alanlar dolu gelir), çift güvence.</summary>
    [Parameter] public ProductGetDto? ProductDefaults { get; set; }

    /// <summary>Graf değişti — parent (ProductLayout) EditChanged'i tetikler (Save aktifliği).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    [Inject] private ISalesChannelTrN11ProductAppService AppService { get; set; } = default!;
    [Inject] private ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelTrN11ProductDto>? _drill;
    private List<SalesChannelListDto> _channels = new();

    protected override async Task OnInitializedAsync()
    {
        // N11 kanalları (kanal adı çözümü + yeni kayıtta otomatik kanal ataması; şirkette TEK N11 kanalı kuralı).
        var paged = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto { MaxResultCount = 1000 });
        _channels = paged.Items.Where(c => c.ChannelType == SalesChannelType.TrN11).ToList();
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

    /// <summary>N11 kanal ürünü taslağı — TEK üretim yeri (split akışı + NewItemFactory aynı default'ları alır).
    /// Id boş (yeni graf düğümü); ClientKey DTO ctor'unda üretilir.</summary>
    private SalesChannelTrN11ProductDto BuildNewN11Draft(Guid salesChannelId)
    {
        var draft = new SalesChannelTrN11ProductDto
        {
            ProductId = ProductId,
            SalesChannelId = salesChannelId,
            Condition = N11ProductCondition.New,
            Domestic = true,
            PreparingDay = 1,
            IsActive = true,
        };

        // Ürün-genel varsayılanlardan create-copy: form açılınca alanlar DOLU gelir, kullanıcı düzenler.
        // Kanal-özel/N11-özel alanlara (Category/Attributes/SellerCode/Group...) DOKUNULMAZ — kullanıcı N11'de girer.
        if (ProductDefaults is { } p)
        {
            // ProductCondition (New/Used) → N11ProductCondition (New/Used) eşlemesi (değerler farklı: 0/1 vs 1/2).
            draft.Condition = p.Condition == ProductCondition.Used ? N11ProductCondition.Used : N11ProductCondition.New;
            // Ürünün MENŞEİ ülkesinden türetilen yerli-ürün bayrağı (sunucu hesaplar); menşei yoksa taslak
            // varsayılanı korunur — bilinmiyorken ithal beyan etmeyelim.
            draft.Domestic = p.IsDomestic ?? draft.Domestic;
            draft.PreparingDay = p.PreparingDay;
            draft.MaxPurchaseQuantity = p.MaxPurchaseQuantity;
            draft.SellerNote = p.SellerNote;
            draft.CurrencyUnitId = p.CurrencyUnitId;
            draft.ProductionDate = p.ProductionDate;
            draft.ExpirationDate = p.ExpirationDate;
            draft.Description = p.Description;
            // Özel bilgi listesi kopyalanır (yeni DTO satırları; ClientKey ctor'da üretilir) — referans paylaşılmaz.
            draft.SpecialInfo = p.SpecialInfo
                .Select(s => new SalesChannelTrN11ProductSpecialInfoDto { Key = s.Key, Value = s.Value })
                .ToList();
        }

        return draft;
    }

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

    // Satır push: yalnız KAYDEDİLMİŞ (Id'li) satırda. Yeni satır (Id boş) → önce ürünü kaydet uyarısı.
    private async Task PushAsync(SalesChannelTrN11ProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var pushed = await AppService.PushToN11Async(channelProduct.Id);
            CopyStatusInto(channelProduct, pushed);
            UiService.ShowSuccessToast(L["N11Product:PushSuccess"].Value);

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

    // Satır stok/fiyat senkronu: yalnız KAYDEDİLMİŞ satırda.
    private async Task SyncStockPriceAsync(SalesChannelTrN11ProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            UiService.ShowWarningToast(L["N11Product:SaveProductFirst"].Value);
            return;
        }

        try
        {
            var synced = await AppService.SyncStockAndPriceAsync(channelProduct.Id);
            CopyStatusInto(channelProduct, synced);
            UiService.ShowSuccessToast(L["N11Product:SyncStockPriceSuccess"].Value);

            foreach (var warning in synced.SyncWarnings)
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

    // Push/sync sonrası N11 durumunu (read-only) grafteki satıra yansıt — reload yok (in-memory graf).
    private static void CopyStatusInto(SalesChannelTrN11ProductDto target, SalesChannelTrN11ProductDto source)
    {
        target.N11ProductId = source.N11ProductId;
        target.SaleStatus = source.SaleStatus;
        target.ApprovalStatus = source.ApprovalStatus;
        target.LastSyncedAt = source.LastSyncedAt;
        target.LastError = source.LastError;
        target.Skus = source.Skus;
    }

    private string ChannelCodeOf(SalesChannelTrN11ProductDto channelProduct)
    {
        return _channels.FirstOrDefault(c => c.Id == channelProduct.SalesChannelId)?.Code ?? string.Empty;
    }

    // Kaydedilmemiş (Id boş) → "Kaydedilmedi"; gönderilmemiş → "Gönderilmedi"; gönderildiyse durum.
    private string StatusTextOf(SalesChannelTrN11ProductDto channelProduct)
    {
        if (channelProduct.Id == Guid.Empty)
        {
            return L["N11Product:NotSaved"].Value;
        }

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
