using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Blazor.Client.Components.Mdi;

public partial class MdiTabHost : IDisposable
{
    [Inject] private ITabManager TabManager { get; set; } = default!;
    [Inject] private IPopupService PopupService { get; set; } = default!;
    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IStringLocalizer<TradeXpressResource> L { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<MdiTabHost> Logger { get; set; } = default!;

    /// <summary>Persist hatası circuit başına TEK toast'la bildirilir (spam yok); her hata yine loglanır.</summary>
    private bool _persistFailureNotified;

    protected override void OnInitialized()
    {
        TabManager.StateChanged += OnStateChanged;
        TabManager.RestoreFailed += OnRestoreFailed;
        TabManager.PersistFailed += OnPersistFailed;
    }

    // Sekme geri yükleme tamamlandı mı — false iken açılış ekranı (splash) gösterilir. İlk render'da
    // TabManager.Tabs HENÜZ BOŞTUR (yükleme OnAfterRenderAsync'te asenkron başlar); bu bayrak olmadan
    // kullanıcı önce "sekmesiz" bir kabuk görüp sonra sekmelerin belirmesini izliyordu.
    private bool _tabsRestored;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Kurtarma kapısı: adres çubuğuna /reset-tabs yazılırsa restore HİÇ denenmeden kayıt sıfırlanır.
            // (Bozuk persisted state açılışı kilitliyorsa tek çıkış yolu budur — MainLayout @Body render
            // etmediğinden routed bir sıfırlama sayfası çalışamaz; ayrıca Ayarlar panelinde buton var.)
            if (new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/').EndsWith("/reset-tabs", StringComparison.OrdinalIgnoreCase))
            {
                await TabManager.HardResetAsync();
                // KRİTİK: MDI gezinmesi adres çubuğunu hiç değiştirmez → sıfırlama sonrası URL burada
                // KALIRSA her sonraki F5/yeniden bağlanma bu dalı tekrar tetikleyip yeni açılan sekmeleri
                // de sessizce siler. replace:true — kurtarma bir geçmiş girdisi bırakmasın.
                Navigation.NavigateTo("/", replace: true);
            }
            else
            {
                // Ana sayfa otomatik sekme olarak açılmaz, null gönderiyoruz
                await TabManager.InitializeAsync(null, null, null);
            }

            // Geri yükleme BİTTİ (başarılı ya da değil) — açılış ekranı kalkar. finally değil burada:
            // yukarıdaki dallar hata fırlatırsa AutoRecoverErrorBoundary devreye girer ve zaten bu bileşen
            // çizilmez; bayrağı orada da açmak, hata ekranının arkasında boş kabuk göstermek olurdu.
            _tabsRestored = true;

            // Server mode: ensure re-render after initialization completes.
            await InvokeAsync(StateHasChanged);
        }
    }

    // TabManager model-kimliğiyle aktif sekmeyi tutar; DxTabs pozisyonel int ister → eşle.
    private int ActiveIndex
    {
        get
        {
            if (TabManager.ActiveTabId is not { } id) return 0;
            for (int i = 0; i < TabManager.Tabs.Count; i++)
                if (TabManager.Tabs[i].Id == id) return i;
            return 0;
        }
    }

    private void OnActiveTabChanged(int index)
    {
        if (index >= 0 && index < TabManager.Tabs.Count)
            TabManager.Activate(TabManager.Tabs[index].Id);
    }

    // X butonu / Delete tuşu → senkron kapatmayı iptal et, kapatmayı dispatcher üzerinden asenkron
    // TryCloseAsync'e devret (async void handler yerine). Atılan Task'ı bilerek gözlemliyoruz (try/catch +
    // log) — aksi halde CanCloseAsync/ConfirmAsync zincirinde bir istisna kimseye ulaşmadan yutulurdu.
    private void OnTabClosing(TabCloseEventArgs e)
    {
        e.Cancel = true; // DxTabs'ın senkron olarak sekmeyi uçurmasını engelle
        if (e.TabIndex >= 0 && e.TabIndex < TabManager.Tabs.Count)
        {
            var tabId = TabManager.Tabs[e.TabIndex].Id;
            _ = InvokeAsync(() => CloseTabSafeAsync(tabId));
        }
    }

    private async Task CloseTabSafeAsync(Guid tabId)
    {
        try
        {
            await TabManager.TryCloseAsync(tabId);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Sekme kapatma isteği başarısız oldu (tabId={TabId}).", tabId);
        }
    }

    private void OnStateChanged() => _ = InvokeAsync(StateHasChanged);

    // ── Kalıcılık geri bildirimleri (toast'lar dispatcher'a alınır — event'ler arka plan Task'ından gelebilir) ──

    private void OnRestoreFailed()
        => _ = InvokeAsync(() => UiService.ShowErrorToast(L["MdiTabsRestoreFailed"].Value));

    private void OnPersistFailed()
    {
        if (_persistFailureNotified) return;
        _persistFailureNotified = true;
        _ = InvokeAsync(() => UiService.ShowWarningToast(L["MdiTabsPersistFailed"].Value));
    }

    // ── Sekme context menüsü (Chrome benzeri) ───────────────────────────────
    private DxContextMenu? _tabMenu;
    private Guid _ctxTabId;

    /// <summary>Sağ-tıklanan sekmenin pin durumuna göre menü metni (Sabitle / Sabitlemeyi kaldır).</summary>
    private string PinMenuText
        => TabManager.Tabs.FirstOrDefault(t => t.Id == _ctxTabId)?.IsPinned == true
            ? L["Tab_Unpin"].Value
            : L["Tab_Pin"].Value;

    private async Task OnTabContextMenuAsync(MouseEventArgs e, MdiTab tab)
    {
        _ctxTabId = tab.Id;                         // sağ-tıklanan sekme (aktif olmak zorunda değil)
        if (_tabMenu is not null)
            await _tabMenu.ShowAsync(e);            // imleç konumunda aç
    }

    private async Task OnCtxCloseAsync() => await TabManager.TryCloseAsync(_ctxTabId);

    // Toplu kapatma: dirty YOK → sessizce hepsi; dirty VAR → TEK uyarı (mevcut ConfirmAsync popup'ı):
    //   Yes = Kaydedilmişleri kapat (yalnız temizler) · No = Yine de kapat (hepsi, dirty atılır) · çarpı = İptal.
    private async Task BulkCloseAsync(TabCloseScope scope)
    {
        var targets = TabManager.GetCloseTargets(scope, _ctxTabId);
        if (targets.Count == 0) return;

        var dirtyCount = targets.Count(t => t.IsDirty);
        if (dirtyCount == 0)
        {
            TabManager.CloseMany(targets.Select(t => t.Id));            // hiç dirty yok → sessizce kapat
            return;
        }

        var result = await UiService.ConfirmAsync(
            string.Format(L["BulkCloseDirtyWarning"].Value, dirtyCount),
            title: null,
            yesText: L["CloseSavedTabs"].Value,     // Kaydedilmişleri kapat (primary)
            noText: L["CloseAnyway"].Value,         // Yine de kapat
            showCancel: false,                      // İptal görevi = popup çarpısı
            defaultYes: true);

        if (result == ConfirmDialogResult.Yes)
        {
            TabManager.CloseMany(targets.Where(t => !t.IsDirty).Select(t => t.Id));   // sadece temiz (kaydedilmiş) olanlar
            // İlk kaydedilmemiş (dirty) sekmeye odaklan — yoksa CloseMany aktifi kapsam-dışı/ilk kalan sekmeye düşürür.
            var firstDirty = targets.FirstOrDefault(t => t.IsDirty);
            if (firstDirty != null) TabManager.Activate(firstDirty.Id);
        }
        else if (result == ConfirmDialogResult.No)
            TabManager.CloseMany(targets.Select(t => t.Id));                          // hepsi — dirty değişiklikler atılır
        // ConfirmDialogResult.Cancel → hiçbir şey yapma
    }

    // Pop-out: sekmeyi kapat (dirty guard) → aynı içeriği popup olarak aç (Dock-to-Tab geri-dönüşü için _TabUrl taşınır).
    private async Task PopOutContextTabAsync()
    {
        var tab = TabManager.Tabs.FirstOrDefault(t => t.Id == _ctxTabId);
        if (tab is null || tab.PageType is null) return;
        if (!await TabManager.TryCloseAsync(tab.Id)) return;

        var parameters = tab.Parameters != null
            ? new Dictionary<string, object>(tab.Parameters)
            : new Dictionary<string, object>();
        parameters["_TabUrl"] = tab.Url;

        await PopupService.ShowAsync(tab.PageType, parameters, new PopupOptions { Title = tab.Title });
    }

    public void Dispose()
    {
        TabManager.StateChanged -= OnStateChanged;
        TabManager.RestoreFailed -= OnRestoreFailed;
        TabManager.PersistFailed -= OnPersistFailed;
    }
}
