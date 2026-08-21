using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.SalesChannelProducts;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.AspNetCore.Components;
using Volo.Abp;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannelProducts;

/// <summary>
/// Kanal ürünleri paneli — kanal-ürün kayıtlarının TEK listeleme bileşeni (kanal edit formu sekmesi +
/// standalone sayfa). PERSISTENT <see cref="DrillList{TItem}"/>: standart araç çubuğu, satır tıkla →
/// düzenleme popup'ı, kolon düzeni kalıcılığı uygulamanın geri kalanıyla aynı.
///
/// <para><b>Neden sunucu sayfalaması YOK:</b> birleşik okuma servisi üç ayrı tabloyu zaten BELLEKTE
/// birleştiriyor (ortak sorgu kökü yok — gerekçesi <c>SalesChannelProductAppService</c> özetinde), yani
/// veri her hâlükârda tümüyle materyalize oluyor. Bu durumda DrillList'in in-memory listesi ek bir maliyet
/// getirmez ve karşılığında uygulamanın standart liste davranışını devralırız. Kayıt sayısı beş haneye
/// çıkarsa çözüm ikisini birden değiştirmektir (SQL UNION + sunucu sayfalaması), yalnız bu paneli değil.</para>
///
/// <para><b>Yeni ekleme KAPALI:</b> kanal-ürün bağı buradan kurulmaz (ürün formundan ya da pazaryeri içe
/// aktarımından gelir — fiyat/stok/görsel oradadır). Silme AÇIK ve yalnız YERELDİR.</para>
/// </summary>
public partial class ChannelProductsPanel
{
    /// <summary>Tek kanala daralt (kanal edit formu bunu verir). Boş → tüm kanallar (standalone liste).</summary>
    [Parameter] public Guid? SalesChannelId { get; set; }

    /// <summary>Kanal TÜRÜNE daralt (opsiyonel; standalone listede tür süzgeci için).</summary>
    [Parameter] public SalesChannelType? ChannelType { get; set; }

    /// <summary>Grid kolon düzeni kalıcılık anahtarı — iki ekran farklı kolon setleri gösterdiğinden
    /// (kanal kolonu) ayrı anahtar kullanır; aksi halde formda gizlenen kolon standalone listede de
    /// gizli kalırdı.</summary>
    // v6: kolon seti değişti (Kanal Engeli + Kampanya eklendi, indeksler kaydı). Kaydedilmiş eski düzen
    // yeni kolonları bilmediği için onları gizli/yanlış yerde çizerdi — anahtar sürümlenerek düzen sıfırlanır.
    // v7 (2026-08-19): İşlemler kolonu eklendi (ChannelProductActions) — aynı gerekçe.
    [Parameter] public string StateKey { get; set; } = "sales-channel-products:list:v7";

    /// <summary>Kanal kolonu görünür mü. Varsayılan: tek kanala daraltılmadıysa görünür — tek kanalda
    /// her satırda aynı değeri tekrarlamak yer israfıdır.</summary>
    private bool ShowChannelColumn
    {
        get { return SalesChannelId is null; }
    }

    // NOT: dar-ekran bayrağı (IsMobile) ARTIK ALINMIYOR. Tek kullanıcısı araç çubuğu etiketlerini dar ekranda
    // gizlemekti; o iş DevExpress'in kendi adaptif davranışına devredildi (BuildCustomActions'taki gerekçe).
    // Ölü bir cascade parametresi zararsız değildi: kabuk her breakpoint değişiminde parametre ataması
    // tetikliyor ve bu panel o yüzden gereksiz yere yeniden çiziliyordu.

    [Inject] private ISalesChannelProductAppService AppService { get; set; } = default!;
    [Inject] private ISalesChannelTrN11ProductAppService N11AppService { get; set; } = default!;
    [Inject] private ISalesChannelTrTrendyolProductAppService TrendyolAppService { get; set; } = default!;
    [Inject] private ISalesChannelEtsyProductAppService EtsyAppService { get; set; } = default!;
    [Inject] private IUiInteractionService Ui { get; set; } = default!;

    // Ürün formunu MERKEZÎ yoldan açar (ham DxPopup YASAK — boardyla aynı desen).
    [Inject] private IViewOpener ViewOpener { get; set; } = default!;
    [Inject] private IPopupService PopupService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    private DrillList<SalesChannelProductListDto>? _drill;

    /// <summary>Açık düzenleme bileşeni — kaydetme buna delege edilir (tipli model orada yaşar).</summary>
    private ChannelProductEditFields? _editFields;

    private ChannelProductPushHistoryPopup? _historyPopup;

    /// <summary>Grid'de seçili satır — araç çubuğundaki "Gönderim Geçmişi" bunun üzerinden açılır.</summary>
    private SalesChannelProductListDto? _selected;

    private List<SalesChannelProductListDto> _rows = new();
    private bool _loading;

    /// <summary>Nötr senkron durumu süzgeci (null = hepsi) — sunucuya tipli eksen olarak gider.</summary>
    private ChannelProductSyncState? _syncState;

    /// <summary>Durum süzgeci araç-çubuğu şablonu — Razor inline template olduğu için .razor'da atanır.</summary>
    private RenderFragment<IToolbarItemInfo>? SyncStateFilterTemplate { get; set; }

    /// <summary>Yüklenmiş verinin KAPSAMI — yeniden çekimin gerekip gerekmediği buna bakılarak anlaşılır.</summary>
    private (Guid? Channel, SalesChannelType? Type)? _loadedScope;

    /// <summary>Yalnız KAPSAM değiştiğinde yeniden çeker.
    ///
    /// <para><b>Neden koşullu:</b> <c>OnParametersSetAsync</c> her parametre atamasında çalışır — ebeveynin
    /// her re-render'ı ve her cascading değer değişimi dahil. Koşulsuz çağrı, kapsam hiç değişmese bile TAM
    /// bir sunucu çekimine dönüşürdü; bu panel dar-ekran bayrağını cascade ile aldığı dönemde pencereyi
    /// 768px eşiğinden geçirmek bile listeyi baştan çektiriyordu. Bayrak artık alınmıyor ama gerekçe DURUYOR:
    /// koruma cascade'e değil, "kapsam değişmediyse çekme" kuralına dayanır.</para></summary>
    protected override async Task OnParametersSetAsync()
    {
        var scope = (SalesChannelId, ChannelType);
        if (_loadedScope == scope)
        {
            return;
        }

        _loadedScope = scope;
        await ReloadAsync();
    }

    // ── Veri ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Listeyi sunucudan tazeler. <see cref="ListRequestDto.AllPages"/> ile TÜM satırlar istenir:
    /// drill in-memory çalışır ve sayfalama/arama grid'in kendi işidir (gerekçe: tip özeti).</summary>
    public async Task ReloadAsync()
    {
        _loading = true;
        StateHasChanged();   // "yükleniyor" görünsün — aksi halde yavaş çekimde ekran sessizce donuk kalır
        try
        {
            var result = await AppService.GetListAsync(new SalesChannelProductListRequestDto
            {
                SalesChannelId = SalesChannelId,
                ChannelType    = ChannelType,
                SyncState      = _syncState,
                MaxResultCount = ListRequestDto.AllPages,
            });

            _rows = new List<SalesChannelProductListDto>(result.Items);
        }
        catch (Exception ex)
        {
            // Sunucu hatası LOKALİZE edilir (kod → mesaj); ham ex.Message kullanıcıya çiğ kod gösterirdi.
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    /// <summary>Durum süzgeci — sunucuya gider (durum saklanan kolon değil, türetilir).</summary>
    private async Task OnSyncStateChanged(ChannelProductSyncState? value)
    {
        _syncState = value;
        await ReloadAsync();
        StateHasChanged();
    }

    // ── Kalıcılık callback'leri ──────────────────────────────────────────────────────────────────────

    /// <summary>Kaydetme, açık düzenleme bileşenine DELEGE edilir: tipli model orada yaşar ve doğrulama da
    /// orada yapılır. Bileşen yoksa (ulaşılamaz olmalı) sessizce "kaydedildi" DENMEZ — istisna fırlatılır,
    /// drill popup'ı açık bırakıp mesajı gösterir.</summary>
    private async Task<SalesChannelProductListDto> PersistUpdateAsync(SalesChannelProductListDto row)
    {
        if (_editFields is null)
        {
            throw new BusinessException(
                "SalesChannelProduct:UnsupportedChannel",
                L["SalesChannelProduct:UnsupportedChannel"].Value);
        }

        await _editFields.SaveAsync();

        // Satırın türetilmiş alanları (durum/uzak kimlik) değişmiş olabilir → OnItemSaved listeyi tazeler.
        return row;
    }

    /// <summary>Silme kanalın KENDİ servisine gider. Kanal başına davranış FARKLI ve kullanıcıya AÇIKÇA söylenir:
    /// Trendyol → hem bizden hem PAZARYERİNDEN kaldırılır (2026-08-16 Hakan kararı; kanal reddederse yerel kayıt
    /// geri döner — sunucu tarafı transactional). N11/Etsy → yalnız yerel kayıt silinir, pazaryerindeki listing
    /// KALIR (henüz uzak silme yolu yok). Onay metni bu farkı yazar — "Sil" iki kanalda iki şey demek olduğu hâlde
    /// tek cümleyle geçilseydi kullanıcı Trendyol'da da silmeyi beklerdi (ya da tersi).</summary>
    private async Task PersistDeleteAsync(SalesChannelProductListDto row)
    {
        var confirmKey = row.ChannelType == SalesChannelType.TrTrendyol
            ? "SalesChannelProduct:DeleteConfirmRemote"
            : "SalesChannelProduct:DeleteConfirmLocalOnly";
        var confirmed = await Ui.ConfirmAsync(
            L[confirmKey, row.ProductCode ?? row.Id.ToString()].Value,
            title: null, yesText: L["Delete"].Value, noText: L["Cancel"].Value,
            showCancel: false, defaultYes: false);
        if (confirmed != ConfirmDialogResult.Yes)
        {
            throw new BusinessException("SalesChannelProduct:DeleteCancelled", L["SalesChannelProduct:DeleteCancelled"].Value);
        }

        switch (row.ChannelType)
        {
            case SalesChannelType.TrN11:
                await N11AppService.DeleteAsync(row.Id);
                break;

            case SalesChannelType.TrTrendyol:
                await TrendyolAppService.DeleteAsync(row.Id);
                break;

            case SalesChannelType.Etsy:
                await EtsyAppService.DeleteAsync(row.Id);
                break;

            default:
                throw new BusinessException(
                    "SalesChannelProduct:UnsupportedChannel",
                    L["SalesChannelProduct:UnsupportedChannel"].Value);
        }
    }

    /// <summary>DrillList zorunlu kılar ama UI'dan çağrılmaz (AllowAdd=false) — bağ buradan kurulmaz.</summary>
    private SalesChannelProductListDto NewRow()
    {
        return new SalesChannelProductListDto();
    }

    // ── Araç çubuğu ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Drill'in araç çubuğuna eklenen panel-özel öğeler: nötr durum süzgeci + gönderim geçmişi.</summary>
    private IReadOnlyList<CrudToolbarAction> BuildCustomActions()
    {
        return new List<CrudToolbarAction>
        {
            new()
            {
                SortIndex = 300,
                Visible = SyncStateFilterTemplate is not null,
                Template = SyncStateFilterTemplate,
            },

            // GÖNDERİM GEÇMİŞİ — seçili satırın PushHistory. Satır seçilmeden anlamsız olduğu için
            // gizlenmez, DEVRE DIŞI bırakılır: görünmeyen düğme "böyle bir şey yok" der, soluk düğme
            // "bir satır seç" der. İkincisi doğru bilgi.
            new()
            {
                SortIndex = 310,

                // ETİKET HER ZAMAN VERİLİR — dar ekranda da (2026-08-13 Hakan: "mobilde toolbar menüsünde
                // daraldığında yazıları görünsün").
                //
                // Önceden dar ekranda `Text` NULL'a çekiliyordu ("uzun etiket araç çubuğunu eziyor", 2026-08-10).
                // O ilaç yanlış yerdeydi: DevExpress zaten dar araç çubuğunda etiketi KENDİ gizliyor
                // (ToolbarRenderer'da AdaptivityAutoCollapseItemsToIcons=true) — kanıtı, `Text` taşıyan
                // çerçeve düğmelerinin (Dışa Aktar · Yenile) çubukta ikona düşüp taşma menüsünde YAZIYLA
                // çıkması. Etiketi kaynağında silmek ise öğeyi menüde de yazısız bırakıyordu: kullanıcı
                // taşma menüsünde ne yaptığı belirsiz üç ikon görüyordu ve tooltip dokunmatikte açılmıyor.
                Text = L["SalesChannelProduct:History:Title"].Value,
                Tooltip = L["SalesChannelProduct:History:Tooltip"],
                IconCssClass = TradeXpressIcons.History + " xaf-toolbar-item-icon",
                Enabled = _selected is not null,
                OnClick = OpenHistoryAsync,
            },

            // MAĞAZADAN İÇE AKTAR — pazaryerindeki listelemeleri (fiyat/adet/görsel/kategori dahil) çeker.
            //
            // NEDEN BU ARAÇ ÇUBUĞUNDA (2026-08-10 Hakan): "kanalda şu an ne var" sorusunun cevabı hiç push
            // edilmemiş kayıtta YALNIZ import'tan gelir. Canlıda 224 Trendyol kaydının TAMAMI böyle: fiyat ve
            // adet kolonları, o kayıtlar yeniden içe aktarılana dek boş kalır. Eylemi listenin dışında bir
            // sihirbaz adımına gömmek, kullanıcıyı boş kolona bakıp sebebini arar hâlde bırakıyordu.
            //
            // YALNIZ TEK KANALA DARALTILMIŞ görünümde: standalone listede hangi kanala gidileceği belirsizdir
            // ve "hepsini içe aktar" istenmeyen bir toplu işlem olurdu. Gizlemek yerine göstermemek değil,
            // görünümün kendisi zaten kanal seçtirmiyor → düğme o kipte hiç çizilmez.
            new()
            {
                SortIndex = 320,
                Visible = SalesChannelId is not null,
                Text = L["SalesChannelProduct:Import"].Value,
                Tooltip = L["SalesChannelProduct:ImportTooltip"],
                IconCssClass = TradeXpressIcons.Swap + " xaf-toolbar-item-icon",
                Enabled = !_importing,
                OnClick = ImportFromMarketplaceAsync,
            },
        };
    }

    /// <summary>İçe aktarım sürüyor — düğme yeniden tıklanamasın (çift import yaratmasın).</summary>
    private bool _importing;

    /// <summary>
    /// Pazaryerinden içe aktarır ve listeyi tazeler. Kanal TÜRÜNE göre ilgili servise dağıtılır — üç
    /// pazaryerinin import ucu ortak bir arayüz paylaşmıyor (kanal-ürünlerinin ortak taban entity'si
    /// olmadığı gerçeğinin aynısı).
    ///
    /// <para><b>Kanal türü satırlardan okunur:</b> panel tek kanala daraltılmışken tüm satırlar aynı kanala
    /// aittir. Liste boşsa tür bilinemez (henüz hiç kayıt yok) → <c>ChannelType</c> parametresine düşülür;
    /// o da yoksa kullanıcı bilgilendirilir. Tahminle rastgele bir servisi çağırmak, yanlış pazaryerine
    /// istek atmak demekti.</para>
    /// </summary>
    private async Task ImportFromMarketplaceAsync()
    {
        if (SalesChannelId is not { } channelId || _importing)
        {
            return;
        }

        var channelType = ChannelType ?? (_rows.Count > 0 ? _rows[0].ChannelType : null);
        if (channelType is null)
        {
            Ui.ShowErrorToast(L["SalesChannelProduct:ImportChannelUnknown"]);
            return;
        }

        _importing = true;
        StateHasChanged();
        try
        {
            switch (channelType)
            {
                case SalesChannelType.TrN11:
                    await N11AppService.ImportFromMarketplaceAsync(channelId);
                    break;

                case SalesChannelType.TrTrendyol:
                    await TrendyolAppService.ImportFromMarketplaceAsync(channelId);
                    break;

                case SalesChannelType.Etsy:
                    await EtsyAppService.ImportFromMarketplaceAsync(channelId);
                    break;

                default:
                    // Import ucu OLMAYAN kanal türü — sessizce "başarılı" demek yerine söylenir.
                    Ui.ShowErrorToast(L["SalesChannelProduct:ImportChannelUnknown"]);
                    return;
            }

            Ui.ShowSuccessToast(L["SalesChannelProduct:ImportDone"]);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Ui.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? ex.Message);
        }
        finally
        {
            _importing = false;
            StateHasChanged();
        }
    }

    /// <summary>Drill'in seçim değişimi — geçmiş düğmesinin etkinliği buna bağlı.</summary>
    private void OnSelectionChanged(SalesChannelProductListDto? row)
    {
        _selected = row;
    }

    /// <summary>Kod sütunundan ÜRÜN formunu popup'ta açar (fiyatlandırma board'unın deseni).
    ///
    /// <para>Ürün formu zaten hem ERP reçetesini hem kanal ürünleri grid'ini içerdiğinden, "popup üstü popup"
    /// yığını yeni bir host yazmadan mevcut parçalardan kurulur ve reçete DOĞRU yerde düzenlenir: emtia
    /// satırları alttaki üründe (ERP, otorite), kanal-özel alanlar üstteki kanal formunda.</para></summary>
    private Task OpenProductAsync(SalesChannelProductListDto row)
    {
        if (row.ProductId == Guid.Empty)
        {
            // Öksüz kanal kaydı (ürünü silinmiş) — açacak form yok. Satır listede DURUR (görünür sorun),
            // ama tıklama sessizce hiçbir şey yapmaz; sahte bir form açmak yanıltıcı olurdu.
            return Task.CompletedTask;
        }

        var extra = new Dictionary<string, object>
        {
            { "OnClosed", EventCallback.Factory.Create(this, () => PopupService.Close()) },
        };

        return ViewOpener.OpenAsync(typeof(Products.ProductEditHost), row.ProductId, string.Empty, null, extra);
    }

    private async Task OpenHistoryAsync()
    {
        if (_selected is { } row && _historyPopup is not null)
        {
            await _historyPopup.OpenAsync(row);
        }
    }

    // ── Gösterim ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Düzenleme popup'ının başlığında görünen satır kimliği.</summary>
    private string RowTitle(SalesChannelProductListDto row)
    {
        return row.ProductCode ?? row.ChannelProductCode ?? string.Empty;
    }

    /// <summary>Kanal etiketi: kanal kodu + tür çevirisi. Kod boşsa (kanal silinmiş) yalnız tür yazılır —
    /// satır gizlenmez, öksüzlük görünür kalır.</summary>
    private string ChannelLabel(SalesChannelProductListDto row)
    {
        var type = L[$"Enum:SalesChannelType:{row.ChannelType}"];
        var typeText = type.ResourceNotFound ? row.ChannelType.ToString() : type.Value;

        return string.IsNullOrWhiteSpace(row.SalesChannelCode) ? typeText : $"{row.SalesChannelCode} — {typeText}";
    }

    private string SyncStateLabel(ChannelProductSyncState state)
    {
        return L[$"Enum:ChannelProductSyncState:{state}"].Value;
    }

    /// <summary>Grup satırının içeriğini çizen şablon (razor'da atanır — inline template C#'a taşınamaz).</summary>
    private RenderFragment<GridDataColumnGroupRowTemplateContext>? GroupRowTemplate { get; set; }

    /// <summary>
    /// Gruplanan kolonun HAM değerini okunur metne çevirir.
    ///
    /// <para><b>Neden gerekli:</b> DevExpress grup satırında değeri olduğu gibi yazar. Enum kolonda bu
    /// "NoRecipe", bool kolonda "True" demektir — kullanıcının arayüzün hiçbir yerinde görmediği kelimeler.
    /// Gruplamayı açmak, hücreyi çizen şablonu grup satırında da düşünmeyi gerektirir.</para>
    ///
    /// <para><b>Kapsam DAR tutuldu:</b> yalnız ham hâli anlamsız olan kolonlar (enum · bool) çevrilir; kod,
    /// ad, kategori gibi metin kolonlarında değerin kendisi zaten okunurdur ve onlara dokunmak, ileride
    /// eklenen bir kolonun sessizce boş grup başlığıyla çıkmasına yol açardı — bilinmeyen alan ham değeri
    /// döndürür.</para>
    /// </summary>
    private string GroupValueText(object? value)
    {
        if (value is null)
        {
            return L["SalesChannelProduct:NoValue"].Value;
        }

        if (value is bool flag)
        {
            return flag ? L["Yes"].Value : L["No"].Value;
        }

        if (value is ChannelProductSyncState syncState)
        {
            return SyncStateLabel(syncState);
        }

        if (value is ChannelProductReadiness readiness)
        {
            return L[$"Enum:ChannelProductReadiness:{readiness}"].Value;
        }

        if (value is ChannelListingObstacle obstacle)
        {
            return ObstacleLabel(obstacle);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>Pazaryeri engelinin okunur adı. <see cref="ChannelListingObstacle.None"/> "engel yok" değil
    /// "bilinen engel yok" der — Trendyol dışı kanallar böyle bir beyan döndürmüyor.</summary>
    private string ObstacleLabel(ChannelListingObstacle obstacle)
    {
        return L[$"Enum:ChannelListingObstacle:{obstacle}"].Value;
    }

    /// <summary>Kanal, bizim son gönderimimizden SONRA değişmiş mi — kanalda bize ait olmayan bir müdahalenin
    /// tek kanıtı. Hiç gönderim yapılmamışsa kıyas edilecek bir an yoktur ve soru KONUSUZDUR: içe aktarılan
    /// her kayıt aksi hâlde "değişti" görünür ve uyarı anlamını yitirirdi.</summary>
    private static bool ChangedOnChannelAfterUs(SalesChannelProductListDto row)
    {
        return row.LastSyncedAt is { } ours
               && row.RemoteUpdatedAt is { } theirs
               && theirs > ours;
    }

    /// <summary>Durum süzgeci seçenekleri — "Tümü" seçeneği LİSTEDE YOK: combo'nun temizle düğmesi
    /// (NullText + ClearButton) zaten o anlama gelir; iki ayrı "hepsi" temsili kafa karıştırırdı.</summary>
    private IReadOnlyList<SyncStateOption> SyncStateOptions
    {
        get { return _syncStateOptions ??= BuildSyncStateOptions(); }
    }

    private IReadOnlyList<SyncStateOption>? _syncStateOptions;

    private IReadOnlyList<SyncStateOption> BuildSyncStateOptions()
    {
        var states = new[]
        {
            ChannelProductSyncState.NotSent,
            ChannelProductSyncState.Pending,
            ChannelProductSyncState.Sent,
            ChannelProductSyncState.Failed,
        };

        var options = new List<SyncStateOption>(states.Length);
        foreach (var state in states)
        {
            options.Add(new SyncStateOption(state, SyncStateLabel(state)));
        }

        return options;
    }

    /// <summary>Durum rozeti — gelen kutusundaki renk diliyle AYNI: yeşil yalnız gerçekten gönderilmiş
    /// satırda, kırmızı elini bekleyen hatada.</summary>
    private static string SyncBadgeStyle(ChannelProductSyncState state)
    {
        var background = state switch
        {
            ChannelProductSyncState.NotSent => "#64748b",   // gri-mavi — henüz gönderilmedi
            ChannelProductSyncState.Pending => "#f59e0b",   // amber — yolda, akıbeti belirsiz
            ChannelProductSyncState.Sent    => "#16a34a",   // yeşil — pazaryerinde canlı
            ChannelProductSyncState.Imported => "#0ea5e9",   // mavi — pazaryerinde var ama BİZ göndermedik
            ChannelProductSyncState.Failed  => "#dc2626",   // kırmızı — son deneme hata verdi
            _ => "#6b7280",
        };

        return "display:inline-block; padding:2px 8px; border-radius:10px; font-size:12px; "
             + $"font-weight:600; color:#fff; background:{background};";
    }

    /// <summary>Durum combo'sunun satırı — metin lokalize olduğu için çalışma anında doldurulur.</summary>
    private sealed record SyncStateOption(ChannelProductSyncState Value, string Text);
}
