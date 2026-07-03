using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// TEK popup chrome kabuğu (bkz. EditShell.razor açıklaması). Pencere durumu (fullscreen/minimize/modal)
/// kabuğa aittir; guard/dock/persistence mantığı sahibe EventCallback ile delege edilir.
/// Modal + Dock aksiyonları başlık butonu DEĞİL, başlık şeridi context menüsündedir
/// (masaüstü sağ-tık, mobil long-press — long-press tespiti edit-shell.js modülünde).
/// </summary>
public partial class EditShell : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Inline (chrome yok) | Popup (DxPopup chrome).</summary>
    [Parameter] public EditShellMode Mode { get; set; } = EditShellMode.Popup;
    /// <summary>Chrome aksiyon seti (Edit/Drill = Minimize|Fullscreen; Global = +Modal+Dock).
    /// Minimize/Fullscreen başlık butonu; Modal/Dock başlık context-menü öğesi olarak çizilir.</summary>
    [Parameter] public EditShellButtons Buttons { get; set; } = EditShellButtons.Minimize | EditShellButtons.Fullscreen;

    /// <summary>Yapısal 3-satır başlık (EditHeaderView ile çizilir). En öncelikli header kaynağı.</summary>
    [Parameter] public TabHeaderData? Header { get; set; }
    /// <summary>Hazır header fragment'ı (GlobalPopupHost'un form-iter IPopupChrome yolu). Header yoksa kullanılır.</summary>
    [Parameter] public RenderFragment? ChromeHeader { get; set; }
    /// <summary>Düz başlık fallback (Header de ChromeHeader de yoksa; ör. PopupOptions.Title).</summary>
    [Parameter] public string? TitleText { get; set; }
    /// <summary>Header'a "*" yıldızı (yapısal header yolunda).</summary>
    [Parameter] public bool IsDirty { get; set; }

    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    /// <summary>Opsiyonel sabit dip (CrudEditView FooterContentTemplate deseni); verilmezse footer çizilmez.</summary>
    [Parameter] public RenderFragment? Footer { get; set; }

    [Parameter] public string MaxWidth { get; set; } = "720px";
    [Parameter] public string? CssClass { get; set; }
    [Parameter] public bool CloseOnEscape { get; set; } = true;
    [Parameter] public bool CloseOnOutsideClick { get; set; }
    /// <summary>Modal öğesi YOKKEN backdrop (Edit/Drill = true → daima modal). Modal öğesi varsa kullanıcı toggle'ı (_isModal).</summary>
    [Parameter] public bool DefaultModal { get; set; } = true;

    /// <summary>Kapanış ÖNCESİ (Closing) — guard MANTIĞI KABUKTA DEĞİL: sahip dinler, dirty ise <c>args.Cancel=true</c> yapar.</summary>
    [Parameter] public EventCallback<PopupClosingEventArgs> OnClosingRequested { get; set; }
    /// <summary>Kapanış kesinleşti — sahip temizlik yapar (parametresiz).</summary>
    [Parameter] public EventCallback OnClosedConfirmed { get; set; }
    /// <summary>Sekmeye sabitle (yalnız Buttons|Dock iken) — mantık (URL oku/sekme aç) sahipte.</summary>
    [Parameter] public EventCallback OnDockRequested { get; set; }
    /// <summary>Popup açıldıktan sonra (Shown) — sahip ilk input'a odak vb. yapar.</summary>
    [Parameter] public EventCallback OnShownRequested { get; set; }

    // Pencere durumu kabuğa AİT (instance-local; iç içe popup'larda biri fullscreen olunca diğeri etkilenmez).
    private bool _isFullscreen;
    private bool _isMinimized;
    private bool _isModal;   // Global modeless default; context menüdeki Modal öğesi ile toggle
    private bool _prevVisible;

    // Context menü + long-press interop durumu.
    private ElementReference _headerRef;
    private DxContextMenu? _chromeMenu;
    private IJSObjectReference? _module;
    private IJSObjectReference? _longPressSub;
    private DotNetObjectReference<EditShell>? _selfRef;

    // Kapanınca pencere durumunu sıfırla (kullanıcı X'i + programmatik kapanış; eski 3 kabuğun davranışı).
    protected override void OnParametersSet()
    {
        if (_prevVisible && !Visible)
        {
            _isFullscreen = false;
            _isMinimized = false;
            _isModal = false;
        }
        _prevVisible = Visible;
    }

    // Modal öğesi yoksa sabit (Edit/Drill = DefaultModal=true → daima modal); varsa kullanıcı toggle'ına bağlı. Minimize'da backdrop kapalı.
    private bool EffectiveBackdrop
    {
        get { return (Buttons.HasFlag(EditShellButtons.Modal) ? _isModal : DefaultModal) && !_isMinimized; }
    }

    // Context menü yalnız Modal/Dock aksiyonu varken ve minimize DEĞİLKEN (eski butonların hide-on-minimize davranışı).
    private bool HasChromeMenu
    {
        get
        {
            return !_isMinimized
                && (Buttons.HasFlag(EditShellButtons.Modal) || Buttons.HasFlag(EditShellButtons.Dock));
        }
    }

    // padding-right BAŞLIK BUTONU sayısından türer (Modal/Dock artık menüde → başlıkta yer kaplamaz).
    private string HeaderPaddingRight
    {
        get
        {
            var count = 0;
            if (Buttons.HasFlag(EditShellButtons.Minimize)) count++;
            if (Buttons.HasFlag(EditShellButtons.Fullscreen)) count++;
            return $"{count * 28 + 12}px";
        }
    }

    private void ToggleFullscreen() { _isMinimized = false; _isFullscreen = !_isFullscreen; }
    private void ToggleMinimize() { _isFullscreen = false; _isMinimized = !_isMinimized; }
    private void ToggleModal() { _isModal = !_isModal; }
    private void OnHeaderDoubleClick() { if (_isMinimized) ToggleMinimize(); else ToggleFullscreen(); }

    // Masaüstü: başlık şeridine sağ-tık → menü imleç konumunda (MdiTabHost sekme menüsü deseni).
    private async Task OnHeaderContextMenu(MouseEventArgs e)
    {
        if (HasChromeMenu && _chromeMenu is not null)
        {
            await _chromeMenu.ShowAsync(e);
        }
    }

    // Mobil: edit-shell.js long-press tespiti (iOS'ta contextmenu event'i hiç gelmez → JS şart).
    [JSInvokable]
    public async Task OnHeaderLongPress(double clientX, double clientY)
    {
        if (HasChromeMenu && _chromeMenu is not null)
        {
            var menu = _chromeMenu;
            await InvokeAsync(() => menu.ShowAsync(clientX, clientY));
        }
    }

    private async Task OnPopupClosing(PopupClosingEventArgs args)
    {
        if (OnClosingRequested.HasDelegate) await OnClosingRequested.InvokeAsync(args);
    }

    private async Task OnPopupClosed()
    {
        await DetachLongPressAsync();
        if (OnClosedConfirmed.HasDelegate) await OnClosedConfirmed.InvokeAsync();
    }

    private async Task OnDockClick()
    {
        if (OnDockRequested.HasDelegate) await OnDockRequested.InvokeAsync();
    }

    private async Task OnPopupShown()
    {
        await AttachLongPressAsync();
        if (OnShownRequested.HasDelegate) await OnShownRequested.InvokeAsync();
    }

    // Long-press dinleyicisi popup açıkken bağlı kalır (Shown'da tak, Closed/Dispose'da sök).
    private async Task AttachLongPressAsync()
    {
        if (_longPressSub is not null) return;
        if (!(Buttons.HasFlag(EditShellButtons.Modal) || Buttons.HasFlag(EditShellButtons.Dock))) return;

        _module ??= await JS.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Integration.Framework.Blazor.Client/js/edit-shell.js");
        _selfRef ??= DotNetObjectReference.Create(this);
        _longPressSub = await _module.InvokeAsync<IJSObjectReference>("attachLongPress", _headerRef, _selfRef, 500);
    }

    private async Task DetachLongPressAsync()
    {
        if (_longPressSub is null) return;
        var sub = _longPressSub;
        _longPressSub = null;
        try
        {
            await sub.InvokeVoidAsync("dispose");
            await sub.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Devre koptu (sekme kapandı) — JS tarafı zaten gitti, sökülecek bir şey yok.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DetachLongPressAsync();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // Devre koptu — modül referansı sunucu tarafında serbest bırakılır.
            }
            _module = null;
        }
        _selfRef?.Dispose();
        _selfRef = null;
    }
}
