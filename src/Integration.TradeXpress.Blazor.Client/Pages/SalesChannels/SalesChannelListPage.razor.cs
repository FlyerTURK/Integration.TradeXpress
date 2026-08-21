using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>
/// Birleşik (polymorphic) satış kanalı listesi — tek grid + "Yeni ▾" tür seçici + tür-bağımlı düzenleme yönlendirmesi.
/// CrudPageBase KULLANMAZ (birleşik servis ICrudAppService değil); CrudLayout'u doğrudan sürer: liste
/// <see cref="ISalesChannelAppService"/> (base sorgusu + tür-bağımsız silme), tipe-özel oluştur/düzelt edit host'larda.
/// </summary>
public partial class SalesChannelListPage : IDisposable
{
    [Inject] protected ISalesChannelAppService SalesChannelAppService { get; set; } = default!;
    [Inject] protected IViewOpener ViewOpener { get; set; } = default!;
    [Inject] public ICrudStateService<SalesChannelListDto, Guid> StateService { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;
    [Inject] protected IPopupService PopupService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IEntityChangeNotifier? EntityChanges { get; set; }

    [CascadingParameter(Name = "CurrentMdiTab")]
    public IMdiTab? CurrentMdiTab { get; set; }

    // Sekmeler arası değişim anahtarı — tipe-özel edit host'lar TListDto=SalesChannelListDto ile Notify eder → eşleşir.
    private string EntityChangeKey => typeof(SalesChannelListDto).FullName ?? nameof(SalesChannelListDto);

    private IReadOnlyList<CrudToolbarAction>? _customActions;

    // Şirkette hâlihazırda bulunan kanal türleri — "Yeni ▾"'de o türü devre dışı bırakır (her türden en fazla bir tane).
    private HashSet<SalesChannelType> _existingTypes = new();

    /// <summary>Şirketin kanalları (kimlik + kod + tür) — "İçe Aktar ▾" alt öğelerini besler. Türlerle
    /// birlikte tek çekimde tazelenir; ayrı bir yükleme yolu açmak iki kaynağın ayrışmasına yol açardı.</summary>
    private List<(Guid Id, string Code, SalesChannelType Type)> _channels = new();

    [Inject] private ChannelImportRunner ImportRunner { get; set; } = default!;

    private GridListDataSource<SalesChannelListDto>? _gridDataSource;

    /// <summary>Server-side grid kaynağı — birleşik <see cref="ISalesChannelAppService.GetListAsync"/>'e bağlı.</summary>
    public GridListDataSource<SalesChannelListDto> GridDataSource
        => _gridDataSource ??= new GridListDataSource<SalesChannelListDto>(FetchPageAsync)
        { 
            OnError = ex => InvokeAsync(() => 
            {
                UiService?.ShowErrorToast(ex.Message);
                StateHasChanged();
            }) 
        };

    private Task<PagedResultDto<SalesChannelListDto>> FetchPageAsync(ListRequestDto request)
    {
        var typed = new SalesChannelListRequestDto
        {
            SkipCount      = request.SkipCount,
            MaxResultCount = request.MaxResultCount,
            Sorting        = request.Sorting,
            Filter         = request.Filter,
            Sorts          = request.Sorts,
            Filters        = request.Filters,
            IsActive       = request.IsActive,
        };
        return SalesChannelAppService.GetListAsync(typed);
    }

    protected override async Task OnInitializedAsync()
    {
        // Server-side Blazor: UI yetki bayrakları serbest, gerçek yetki server API'sinde (Product/CrudPageBase deseni).
        StateService.IsGrantedCreate = StateService.IsGrantedUpdate = StateService.IsGrantedDelete = true;
        StateService.OnStateChanged += OnStateChangedHandler;

        if (EntityChanges != null)
        {
            EntityChanges.EntityChanged += OnEntityChangedExternally;
        }

        await RefreshAvailableTypesAsync();   // ilk "Yeni ▾" durumları (mevcut türler devre dışı)
    }

    // Mevcut türleri server'dan çek + "Yeni ▾" aksiyonlarını yeniden kur. Her reload'da (kaydet/sil/tazele) çağrılır.
    private async Task RefreshAvailableTypesAsync()
    {
        try
        {
            _existingTypes = new HashSet<SalesChannelType>(await SalesChannelAppService.GetExistingChannelTypesAsync());

            var channelPage = await SalesChannelAppService.GetListAsync(new SalesChannelListRequestDto
            {
                MaxResultCount = ListRequestDto.AllPages,
            });
            _channels = channelPage.Items
                .Select(c => (c.Id, c.Code, c.ChannelType))
                .ToList();
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }

        _customActions = BuildCustomActions();
    }

    // "Yeni ▾" tür seçici (stok Yeni gizli) — alt item'lar tipe-özel edit host'u açar. SortIndex=0 → en solda.
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions() => new List<CrudToolbarAction>
    {
        new()
        {
            SortIndex = 0, Text = L["New"], Tooltip = L["New"],
            IconCssClass = TradeXpressIcons.Add + " xaf-toolbar-item-icon",
            Items = new List<CrudToolbarAction>
            {
                NewTypeItem(SalesChannelType.TrN11),
                NewTypeItem(SalesChannelType.TrTrendyol),
                NewTypeItem(SalesChannelType.Etsy),
            },
        },

        // KANAL ÜRÜNLERİ — bağımsız liste. Kanal SEÇİMİ GEREKTİRMEZ (satır seçili olmasa da açılır):
        // sayfa tüm kanalların kayıtlarını gösterir, daraltma orada yapılır. Kanal edit formundaki liste
        // ile AYNI bileşeni sürer; bu düğme yalnız ona kanal-bağımsız bir giriş noktası açar.
        // SAYFA olduğu için popup yolundan DEĞİL sekmeden/adres çubuğundan açılır (sihirbazla aynı gerekçe).
        new()
        {
            SortIndex = 1,
            Text = L["SalesChannelProducts"],
            Tooltip = L["SalesChannelProducts:Tooltip"],
            IconCssClass = TradeXpressIcons.Product + " xaf-toolbar-item-icon",
            OnClick = () => OpenRouteAsync("/sales-channel-products", L["SalesChannelProducts"].Value),
        },

        // MAĞAZADAN İÇE AKTAR (2026-08-10 Hakan) — şirketin KANALLARI alt öğe olarak listelenir.
        // Neden seçili satıra bağlı DEĞİL: bu liste satır seçimi tutmuyor ve her türden en fazla bir kanal
        // olduğundan alt liste zaten en çok üç öğe; seçim mekanizması eklemek, üç öğelik bir menü için
        // yeni bir durum kavramı getirmek olurdu.
        new()
        {
            SortIndex = 2,
            Text = L["SalesChannel:ImportProducts"],
            Tooltip = L["SalesChannel:ImportProducts"],
            IconCssClass = TradeXpressIcons.Swap + " xaf-toolbar-item-icon",
            Enabled = _channels.Count > 0,
            Items = _channels.ConvertAll(ImportChannelItem),
        },
    };

    /// <summary>Bir kanalın içe aktarımını çalıştıran alt menü öğesi.</summary>
    private CrudToolbarAction ImportChannelItem((Guid Id, string Code, SalesChannelType Type) channel)
    {
        return new CrudToolbarAction
        {
            Text = string.Concat(channel.Code, " - ", ChannelTypeLabel(channel.Type)),
            Tooltip = L["SalesChannel:ImportProducts"],
            IconCssClass = TradeXpressIcons.SalesChannel + " xaf-toolbar-item-icon",
            OnClick = () => RunImportAsync(channel.Id, channel.Type),
        };
    }

    /// <summary>İçe aktarımı çalıştırır ve sonucu SÖYLER. Ortak koşucu kullanılır (ChannelImportRunner) —
    /// kanal edit formundaki düğmeyle aynı yol; tür dağıtımı iki yerde ayrı yaşamaz.</summary>
    private async Task RunImportAsync(Guid channelId, SalesChannelType type)
    {
        try
        {
            var outcome = await ImportRunner.RunAsync(channelId, type);
            UiService?.ShowSuccessToast(outcome.Supported
                ? L["SalesChannel:ImportDone", outcome.Created, outcome.Updated].Value
                : L["SalesChannel:ImportUnsupported"].Value);
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
    }

    // "Yeni ▾" alt item — tür şirkette zaten varsa DEVRE DIŞI (tooltip açıklar); server de Create'te zorlar.
    private CrudToolbarAction NewTypeItem(SalesChannelType type)
    {
        var exists = _existingTypes.Contains(type);
        return new CrudToolbarAction
        {
            Text = ChannelTypeLabel(type),
            Tooltip = exists ? L["TradeXpress:SalesChannel:TypeAlreadyExists"] : ChannelTypeLabel(type),
            Enabled = !exists,
            IconCssClass = TradeXpressIcons.SalesChannel + " xaf-toolbar-item-icon",
            OnClick = () => OpenEditAsync(type, null),
        };
    }

    private string ChannelTypeLabel(SalesChannelType type) => type switch
    {
        SalesChannelType.TrTrendyol => L["SalesChannelType:TrTrendyol"],
        SalesChannelType.Etsy => L["SalesChannelType:Etsy"],
        _ => L["SalesChannelType:TrN11"],
    };

    // Satır düzenleme — kaydın türüne göre doğru edit host'a yönlendir.
    private Task RouteEditAsync(SalesChannelListDto item)
    {
        StateService.SetDataRowSelected(item);
        return OpenEditAsync(item.ChannelType, item.Id);
    }

    // Tür → edit host + route. MDI sekmesi varsa sekmede aç (URL eşleşmesiyle çift sekme olmaz), yoksa popup.
    private async Task OpenEditAsync(SalesChannelType type, Guid? id)
    {
        // Savunma: yeni ekleme (id yok) ve tür zaten varsa engelle (dropdown devre dışı olsa da; server de reddeder).
        if (id is null && _existingTypes.Contains(type))
        {
            return;
        }

        var (hostType, basePath) = type switch
        {
            SalesChannelType.TrTrendyol => (typeof(SalesChannelTrTrendyolEditHost), "/sales-channels/trendyol"),
            SalesChannelType.Etsy => (typeof(SalesChannelEtsyEditHost), "/sales-channels/etsy"),
            _ => (typeof(SalesChannelTrN11EditHost), "/sales-channels/n11"),
        };

        var title = ChannelTypeLabel(type);

        // YENİ kanal → kurulum SİHİRBAZI (düzenleme yolu değişmez; mevcut formlar aynen yerinde durur).
        // Gerekçe: kanalı açmak yetmiyor — kurulum kararları (N11: kargo şablonu + KDV, Trendyol: kargo firması)
        // düz formda hiç sorulmuyor ve arkada sessizce koşan adımlar (kategori senkronu, ürün çekimi) kullanıcıya
        // görünmüyor. Sihirbaz SAYFA olduğundan popup yolundan DEĞİL sekmeden/adres çubuğundan açılır.
        if (id is null)
        {
            var wizardRoute = type switch
            {
                SalesChannelType.TrN11 => ("/sales-channels/n11/wizard", L["SalesChannelTrN11:Wizard:Title"].Value),
                SalesChannelType.TrTrendyol => ("/sales-channels/trendyol/wizard", L["SalesChannelTrTrendyol:Wizard:Title"].Value),
                _ => (null, null),   // Etsy: OAuth akışı ayrı — sihirbaza taşınmadı, düz form korunuyor
            };

            if (wizardRoute.Item1 is { } route)
            {
                await OpenRouteAsync(route, wizardRoute.Item2!);
                return;
            }
        }

        if (CurrentMdiTab != null && ServiceProvider.GetService(typeof(IMdiTabOpener)) is IMdiTabOpener tabs)
        {
            var url = id is { } gid ? $"{basePath}/{gid}" : $"{basePath}/new";
            await tabs.OpenOrActivateAsync(url, title, TradeXpressIcons.SalesChannel);
            return;
        }

        var extra = new Dictionary<string, object>
        {
            { "OnSaved",  EventCallback.Factory.Create(this, ReloadAsync) },
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };
        await ViewOpener.OpenAsync(hostType, id, title, TradeXpressIcons.SalesChannel, extra);
    }

    /// <summary>Tam SAYFA olan bir hedefi açar (popup host'u yok): MDI varsa sekmede, yoksa adres çubuğundan.
    /// Sihirbaz gibi çok adımlı akışlar popup'a sığmaz — kullanıcı ortada kalırsa kanal zaten kurulmuş olur.</summary>
    private async Task OpenRouteAsync(string url, string title)
    {
        if (CurrentMdiTab != null && ServiceProvider.GetService(typeof(IMdiTabOpener)) is IMdiTabOpener tabs)
        {
            await tabs.OpenOrActivateAsync(url, title, TradeXpressIcons.SalesChannel);
            return;
        }

        Navigation.NavigateTo(url);
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = StateService.SelectedDataItems?.OfType<SalesChannelListDto>().ToList();
        if (selected == null || selected.Count == 0)
        {
            return;
        }

        var confirmMessage = selected.Count == 1
            ? L["AreYouSureToDelete"]
            : string.Format(L["AreYouSureToDeleteMultiple"], selected.Count);
        var deleteButtonText = selected.Count == 1 ? L["DeleteRecordButton"] : L["DeleteRecordsButton"];

        if (await UiService.ConfirmDeleteAsync(confirmMessage, yesText: deleteButtonText) != ConfirmDialogResult.Yes)
        {
            return;
        }

        try
        {
            StateService.IsBusy = true;
            foreach (var item in selected)
            {
                await SalesChannelAppService.DeleteAsync(item.Id);   // tür-bağımsız (base id; TPT cascade)
            }

            StateService.SelectedDataItems = new List<object>();
            await RefreshAvailableTypesAsync();   // silinen tür yeniden eklenebilir hale gelir
            StateService.RequestReload();
            UiService.ShowSuccessToast(L["SuccessfullyDeleted"]);
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
        finally
        {
            StateService.IsBusy = false;
            StateService.NotifyStateChanged();
        }
    }

    private async Task ReloadAsync()
    {
        await RefreshAvailableTypesAsync();   // kaydet/tazele sonrası "Yeni ▾" durumları güncel kalsın
        StateService.RequestReload();
    }

    private Task OnSearchAsync(string text)
    {
        GridDataSource.SearchText = text;
        StateService.RequestReload();
        return Task.CompletedTask;
    }

    private void OnStateChangedHandler() => InvokeAsync(StateHasChanged);

    // Edit sekmesinde kayıt/silme → aynı anahtarla Notify → grid'i tazele.
    private void OnEntityChangedExternally(string key)
    {
        if (!string.Equals(key, EntityChangeKey, StringComparison.Ordinal))
        {
            return;
        }

        InvokeAsync(async () =>
        {
            await RefreshAvailableTypesAsync();
            StateService.RequestReload();
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        if (StateService != null)
        {
            StateService.OnStateChanged -= OnStateChangedHandler;
        }

        if (EntityChanges != null)
        {
            EntityChanges.EntityChanged -= OnEntityChangedExternally;
        }
    }
}
