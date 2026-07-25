using System.Reflection;
using System.Text.Json;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Merkezî value-object edit popup kabuğu — bkz. <c>ValueObjectEditPopup.razor</c> açıklaması.
/// Entity edit popup'larıyla AYNI chrome bileşenlerini (EditShell + EditHeaderView + EditToolbar) inline kullanır;
/// GlobalPopupHost'un tek global popup slotuna girmediğinden parent entity edit popup'ının üstünde NESTED açılır.
/// <para>Minimal <see cref="ISplitEditActions"/> uygular: yalnız Kaydet + Reset görünür (Sil / Kaydet-ve-Yeni /
/// kayıt gezinme / Undo-Redo yeteneklerinin hepsi kapalı). Kaydet davranışı çağrı yerine (<see cref="OnSave"/>) ait;
/// kapatmayı KABUK yapar. Reset/İptal açılış JSON snapshot'ından in-place geri yükler (canlı-bind güvenli).</para>
/// </summary>
public partial class ValueObjectEditPopup<TValue> : CrudComponentBase, ISplitEditActions
    where TValue : class
{
    /// <summary>Düzenlenen value-object (owned sub-object). Snapshot / dirty / reset bunun üstünde çalışır;
    /// gövde (ChildContent) AYNI örneğe canlı-bind eder → geri alım in-place kopyalama ile yapılır.</summary>
    [Parameter, EditorRequired] public TValue Model { get; set; } = default!;

    [Parameter] public bool Visible { get; set; }
    [Parameter] public EventCallback<bool> VisibleChanged { get; set; }

    /// <summary>Popup başlığı (ör. "Gönderim Adresi") — EditHeaderView L1 satırı.</summary>
    [Parameter, EditorRequired] public string HeaderText { get; set; } = default!;

    /// <summary>Başlık ikonu — merkezî ikon setinden çağrı yeri verir (ör. <c>TradeXpressIcons.AddressCard</c>).
    /// Framework <c>TradeXpressIcons</c>'a erişemez → parametre ile alınır (ad-hoc ikon YOK).</summary>
    [Parameter] public string? IconCssClass { get; set; }

    /// <summary>Düzenleme gövdesi (ör. <c>AddressFields</c>) — <see cref="Model"/>'e canlı-bind eder.</summary>
    [Parameter, EditorRequired] public RenderFragment ChildContent { get; set; } = default!;

    /// <summary>Kaydet'te çalışır — davranış çağrı yerine ait (custom adres = uygula/zaten bind; şube-modu = persist).
    /// Popup kapanışını KABUK yapar → bu callback yalnız YAN ETKİDİR (içinde popup'ı kapatma).</summary>
    [Parameter] public EventCallback OnSave { get; set; }

    /// <summary>Popup genişlik üst sınırı (EditShell %95 genişliği bununla kapar). Default 720px — adres formuna yeter.</summary>
    [Parameter] public string MaxWidth { get; set; } = "720px";

    [Inject] private IUiInteractionService UiService { get; set; } = default!;

    private TabHeaderData _header = default!;
    private bool _wasVisible;
    private string? _snapshot;
    private int _bodyKey;
    private bool _busy;

    // Gövdeye cascade edilen doğrulama bağlamı (nested EditForm YOK — DrillList deseni). Model örneği
    // değişince yeniden kurulur; aksi halde eski nesne doğrulanır.
    private EditContext? _editContext;
    private TValue? _contextModel;
    private bool _lastDirty;

    protected override void OnParametersSet()
    {
        _header = new TabHeaderData
        {
            FormCaption = HeaderText,
            IconCssClass = IconCssClass,
        };

        if (Model is not null && !ReferenceEquals(_contextModel, Model))
        {
            _contextModel = Model;
            if (_editContext != null)
            {
                _editContext.OnFieldChanged -= OnEditFieldChanged;
            }

            _editContext = new EditContext(Model);
            _editContext.OnFieldChanged += OnEditFieldChanged;
        }

        // Popup görünür oldu (false→true) → açılış anındaki VO durumunun snapshot'ını al (dirty/reset bazı).
        if (Visible && !_wasVisible)
        {
            CaptureSnapshot();
        }

        _wasVisible = Visible;
    }

    // Açılış anındaki temiz VO durumunu sakla (JSON — flat DTO; EntityEditForm._cleanSnapshot deseni).
    private void CaptureSnapshot()
    {
        try
        {
            _snapshot = JsonSerializer.Serialize(Model);
        }
        catch
        {
            _snapshot = null;
        }

        _lastDirty = IsDirty;
    }

    // Gövdedeki DOM sinyali (@oninput her tuş / @onchange commit) — @bind'lı DevExpress editörleri bu yolla duyulur.
    private void OnBodyChanged()
    {
        NotifyToolbarIfDirtyChanged();
    }

    // EditContext sinyali — ValidationEnabled="false" combolar (GeographyCascadePicker) DOM'a change yaymaz,
    // AddressFields elle NotifyFieldChanged çağırır; toolbar'ın tazelendiği yer burası.
    private void OnEditFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        NotifyToolbarIfDirtyChanged();
    }

    // Her tuşta re-render ETMEZ — yalnız dirty durumu GERÇEKTEN değiştiyse (CrudEditComponentBase'in
    // NotifyToolbarIfChanged deseni). Gereksiz render + odak kaybı yok, buton yine anında doğru duruma geçer.
    private void NotifyToolbarIfDirtyChanged()
    {
        var dirty = IsDirty;
        if (dirty == _lastDirty)
        {
            return;
        }

        _lastDirty = dirty;
        StateHasChanged();
    }

    // Dirty = açılış snapshot'ından fark (EditShell "*" ve toolbar CanSave/Reset-enabled bunu okur).
    private bool IsDirty
    {
        get
        {
            if (_snapshot is null)
            {
                return true;
            }

            try
            {
                return JsonSerializer.Serialize(Model) != _snapshot;
            }
            catch
            {
                return true;
            }
        }
    }

    // Kaydet: yan etkiyi (uygula/persist) uygula, sonra KABUK popup'ı kapatır. Programmatik kapanış → guard atlanır.
    private async Task ApplyAndCloseAsync()
    {
        if (_busy)
        {
            return;
        }

        // Zorunlu alan boşsa KAYDETME (ve kapatma). SESSİZ kalmaz: editörün kendi kırmızı işareti +
        // ValidationSummary + TOAST birlikte duyurur (eskiden hiçbir geri bildirim yoktu — kullanıcı bulgusu).
        if (_editContext is { } context && !context.Validate())
        {
            context.ShowValidationToasts(UiService);
            StateHasChanged(); // inline işaretler/summary Validate() sonrası ekrana insin
            return;
        }

        _busy = true;
        try
        {
            await OnSave.InvokeAsync();
            await VisibleChanged.InvokeAsync(false);
        }
        finally
        {
            _busy = false;
        }
    }

    // Kapanış ÖNCESİ (X / Escape / dış-tık) — kirliyse Kaydet/Yoksay sor; iptal edilirse kapanmayı durdur.
    private async Task OnClosingRequested(PopupClosingEventArgs args)
    {
        if (args.CloseReason == PopupCloseReason.Programmatically)
        {
            return; // Kaydet (VisibleChanged=false) ya da kod zaten karar verdi/uyguladı.
        }

        if (!await ConfirmCloseAsync())
        {
            args.Cancel = true;
        }
    }

    // Temizse serbest; kirliyse Kaydet (uygula/persist + kapat) / Yoksay (snapshot'a in-place geri dön + kapat).
    private async Task<bool> ConfirmCloseAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        var result = await UiService.ConfirmAsync(
            L["UnsavedChangesConfirmation"].Value,
            title: null,
            yesText: L["SaveChanges"].Value,
            noText: L["DiscardChanges"].Value,
            showCancel: false,
            defaultYes: true);

        switch (result)
        {
            case ConfirmDialogResult.Yes:
                await OnSave.InvokeAsync(); // değişiklik korunur (uygula/persist), kapanışa izin ver
                return true;
            case ConfirmDialogResult.No:
                RestoreSnapshot(); // canlı-bind custom adreste parent modelini de temizler
                return true;
            default:
                return false;
        }
    }

    // Açılış snapshot'ını IN-PLACE geri yükle (parent + gövde AYNI örneğe baktığından instance REPLACE edilmez,
    // alanlar geri kopyalanır). Gövde @key bump'ı ile yeniden kurulur → geri alınan değerler ekranda görünür.
    private void RestoreSnapshot()
    {
        if (_snapshot is null)
        {
            return;
        }

        try
        {
            var restored = JsonSerializer.Deserialize<TValue>(_snapshot);
            if (restored is null)
            {
                return;
            }

            CopyInto(restored, Model);
            _bodyKey++;
            _lastDirty = false; // snapshot'a döndük → toolbar temiz duruma geçsin (aksi halde Kaydet enabled kalırdı)
            StateHasChanged();
        }
        catch
        {
            // fail-safe: geri alınamadıysa mevcut durumu bırak.
        }
    }

    // Public yazılabilir scalar property'leri kaynaktan hedefe kopyalar (flat VO DTO — sığ kopya yeter).
    private static void CopyInto(TValue source, TValue target)
    {
        foreach (var property in typeof(TValue).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
            {
                property.SetValue(target, property.GetValue(source));
            }
        }
    }

    // ── ISplitEditActions (minimal VO controller) ──
    // Yalnız Kaydet + Reset görünür; kalan yetenekler kapalı (EditToolbar bayrakları gizler).
    bool ISplitEditActions.CanSave
    {
        get { return IsDirty; }
    }

    bool ISplitEditActions.IsNew
    {
        get { return false; }
    }

    bool ISplitEditActions.IsReadOnly
    {
        get { return false; }
    }

    string? ISplitEditActions.ReadOnlyNotice
    {
        get { return null; }
    }

    Task ISplitEditActions.SaveAsync()
    {
        return ApplyAndCloseAsync();
    }

    bool ISplitEditActions.SupportsSaveAndNew
    {
        get { return false; }
    }

    Task ISplitEditActions.SaveAndNewAsync()
    {
        return Task.CompletedTask;
    }

    Task ISplitEditActions.SaveAndCloseAsync()
    {
        return ApplyAndCloseAsync();
    }

    bool ISplitEditActions.SupportsDelete
    {
        get { return false; }
    }

    bool ISplitEditActions.CanDelete
    {
        get { return false; }
    }

    Task ISplitEditActions.DeleteAsync()
    {
        return Task.CompletedTask;
    }

    bool ISplitEditActions.SupportsRecordNavigation
    {
        get { return false; }
    }

    bool ISplitEditActions.CanGoPrevious
    {
        get { return false; }
    }

    bool ISplitEditActions.CanGoNext
    {
        get { return false; }
    }

    Task ISplitEditActions.GoPreviousAsync()
    {
        return Task.CompletedTask;
    }

    Task ISplitEditActions.GoNextAsync()
    {
        return Task.CompletedTask;
    }

    Task<bool> ISplitEditActions.CanLeaveAsync()
    {
        return ConfirmCloseAsync();
    }

    Task ISplitEditActions.ResetAsync()
    {
        RestoreSnapshot();
        return Task.CompletedTask;
    }

    bool ISplitEditActions.SupportsUndoRedo
    {
        get { return false; }
    }

    bool ISplitEditActions.CanUndo
    {
        get { return false; }
    }

    bool ISplitEditActions.CanRedo
    {
        get { return false; }
    }

    Task ISplitEditActions.UndoAsync()
    {
        return Task.CompletedTask;
    }

    Task ISplitEditActions.RedoAsync()
    {
        return Task.CompletedTask;
    }

    void ISplitEditActions.NotifyInput()
    {
        StateHasChanged();
    }

    void ISplitEditActions.CommitUndoStep()
    {
        // Undo geçmişi yok (SupportsUndoRedo=false).
    }

    // EditContext aboneliğini bırak (EntityEditForm ile aynı desen) — popup her açılışta yeniden kurulduğundan
    // bırakılmayan handler birikirdi.
    public void Dispose()
    {
        if (_editContext != null)
        {
            _editContext.OnFieldChanged -= OnEditFieldChanged;
        }
    }
}
