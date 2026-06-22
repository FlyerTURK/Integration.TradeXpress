using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Integration.Framework.Base.Dtos.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Application.Services;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Yeni Mimari: Sadece kendi verisini çeken, düzenleyen ve kaydeden
/// bağımsız (standalone) Edit formları için temel sınıf.
/// Popup (Modal) veya MDI Sekmesi (Tab) içinde çalışabilir.
/// </summary>
public abstract class CrudEditComponentBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> : CrudComponentBase, ISplitEditActions, IEditHeaderSource, IDisposable
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : class, new()
    where TCreateDto : class, new()
    where TUpdateDto : class, new()
{
    [Parameter]
    public TKey? Id { get; set; }

    [Parameter]
    public bool IsPopupMode { get; set; }

    /// <summary>
    /// SplitCrudView tarafından panel modunda geçilir.
    /// true iken kapatma guard'ı ve CloseAsync atlanır;
    /// SaveAndClose → sadece kayıt yapar (panel sabit kalır).
    /// </summary>
    [Parameter]
    public bool IsEmbedded { get; set; }

    [Parameter]
    public EventCallback OnSaved { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>Embedded modda silme sonrası SplitCrudView paneli sıfırlamak için kullanır.</summary>
    [Parameter]
    public EventCallback OnDeleted { get; set; }

    [CascadingParameter(Name = "CurrentMdiTab")]
    public IMdiTab? CurrentMdiTab { get; set; }

    /// <summary>
    /// SplitCrudView birleşik toolbar host'u. Doluysa bu edit kendi toolbar'ını çizmez
    /// (CrudEditShell aynı cascade'i görüp gizler) ve aksiyonlarını host'a register eder.
    /// </summary>
    [CascadingParameter]
    public ISplitHost? SplitHost { get; set; }

    /// <summary>Liste sayfasıyla PAYLAŞILAN scoped state (kayıtlar, seçim, Prev/Next). Standalone/popup
    /// edit'te kayıtlar arası gezinme bu merkezi servisten yapılır (split'te SplitHost devrede).</summary>
    [Inject] protected ICrudStateService<TListDto, TKey>? StateService { get; set; }

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // ── ISplitEditActions ──
    bool ISplitEditActions.CanSave => !IsReadOnly && IsDirty;
    bool ISplitEditActions.IsNew   => IsNewMode;

    /// <summary>Salt-okunur mod — alt sınıf override eder (ör. tenant'ta global birim). Kaydet/Sil gizlenir,
    /// form devre dışı kalır, üstte <see cref="ReadOnlyNotice"/> banner'ı çıkar.</summary>
    public virtual bool IsReadOnly => false;

    /// <summary>Salt-okunur formdaki bilgilendirme metni; null → CrudEditShell genel mesajı gösterir.</summary>
    public virtual string? ReadOnlyNotice => null;

    bool ISplitEditActions.IsReadOnly => IsReadOnly;
    string? ISplitEditActions.ReadOnlyNotice => ReadOnlyNotice;

    // ── Yapısal başlık (3-satır: tür / kimlik / parent) — TEK kaynak; MDI tab + top-panel + popup tüketir ──
    /// <summary>L1 — entity tür adı. Varsayılan: TGetDto adından "GetDto" atılıp lokalize edilir.</summary>
    protected virtual string EditFormCaption => CrudNaming.EntityCaption(typeof(TGetDto), L);
    /// <summary>L2 — kaydın kimlik değeri. Varsayılan: DTO <see cref="IHasCode"/> ise Code (explicit, reflection yok).
    /// Code'u olmayan/farklı kimlik kullanan entity alt sınıfta override eder.</summary>
    protected virtual string? EditEntityValue => (EditModel as IHasCode)?.Code is { Length: > 0 } code ? code : null;
    /// <summary>L3a — parent etiketi (org entity'leri override eder; ör. "Şirket").</summary>
    protected virtual string? EditParentLabel => null;
    /// <summary>L3b — parent değeri (ör. "MERKEZ").</summary>
    protected virtual string? EditParentValue => null;
    /// <summary>Başlık ikonu (ör. "fas fa-coins").</summary>
    protected virtual string? EditIconCssClass => null;

    /// <summary>Mevcut virtual'lardan yapısal başlığı kurar (NewPrefix=IsNew, IsDirty dahil).</summary>
    protected TabHeaderData BuildEditHeader() => new()
    {
        FormCaption  = EditFormCaption,
        NewPrefix    = IsNewMode ? L["New"].Value : null,
        EntityValue  = IsNewMode ? null : EditEntityValue,
        ParentLabel  = EditParentLabel,
        ParentValue  = EditParentValue,
        IconCssClass = EditIconCssClass,
    };

    TabHeaderData? IEditHeaderSource.EditHeader => BuildEditHeader();
    bool IEditHeaderSource.IsDirty => IsDirty;   // dirty TEK kaynak (popup/tab aynı bayraktan "*")

    // MDI tab başlığını push etmek için (opsiyonel — app TabManager'ı IMdiTabOpener olarak kaydeder).
    private IMdiTabOpener? TabOpener => ServiceProvider.GetService<IMdiTabOpener>();
    private bool _lastSyncedDirty;

    /// <summary>Yapısal başlığı MDI sekmesine push eder (model yüklenince + dirty geçişinde). Popup/split shell
    /// EditHeader'dan canlı okur; bu metot MDI tab + top-panel içindir.</summary>
    protected void SyncTabHeader()
    {
        _lastSyncedDirty = IsDirty;
        if (CurrentMdiTab is not { } tab) return;

        // SplitView/embedded: tab LİSTE sayfasınındır (liste+detay aynı sekme) → başlığı EZMEYİZ; listeleme
        // mantığı korunur. Tek fark: embedded edit'in dirty bayrağı liste tab'ına "*" olarak yansır.
        if (SplitHost != null || IsEmbedded)
            TabOpener?.SetTabDirty(tab.Id, IsDirty);
        else
        {
            TabOpener?.UpdateTabHeader(tab.Id, BuildEditHeader());   // standalone MDI: yapısal başlık
            TabOpener?.SetTabDirty(tab.Id, IsDirty);                 // dirty TEK kaynak = MdiTab.IsDirty
        }
    }

    /// <summary>
    /// EditModel'e yansımayan dirty kaynağı (embedded izin paneli gibi) değişince çağrılır.
    /// <see cref="NotifyToolbarIfChanged"/> yalnız EditModel JSON'u değişince tetiklendiğinden bu kaynağı
    /// göremez; bu yüzden split toolbar + MDI sekme "*" + sayfa render'ını DOĞRUDAN tazeleriz.
    /// </summary>
    protected void NotifyDirtyChanged()
    {
        SplitHost?.NotifyChanged();
        if (IsDirty != _lastSyncedDirty) SyncTabHeader();
        StateHasChanged();
    }

    Task ISplitEditActions.SaveAsync()         => SaveAsync();   // Task<bool> → Task
    Task ISplitEditActions.SaveAndNewAsync()   => SaveAndNewAsync();
    Task ISplitEditActions.SaveAndCloseAsync() => SaveAndCloseAsync();
    Task<bool> ISplitEditActions.CanLeaveAsync() => ConfirmCloseAsync();   // dirty ise discard onayı
    Task ISplitEditActions.ResetAsync() => ResetAsync();
    bool ISplitEditActions.CanUndo => CanUndo;
    bool ISplitEditActions.CanRedo => CanRedo;
    Task ISplitEditActions.UndoAsync() { Undo(); return Task.CompletedTask; }
    Task ISplitEditActions.RedoAsync() { Redo(); return Task.CompletedTask; }

    // ── Sayfa-aşırı gezinme: TEK KÖPRÜ = paylaşılan StateService (split ile aynı CANLI-index prensibi) ──
    // "Neredeyiz" StateService.CurrentGlobalIndex'ten (kayıtlı grid'in canlı GridVisibleKeys + PageSkip'inden,
    // CurrentKeyProvider=()=>Id ile); ayrı stale sayaç YOK. Prev/Next köprünün GoNext/PreviousGlobalAsync'ine
    // delege eder; köprü guard + grid taşıma/odak + OnRecordActivated (Id=Key; LoadDataAsync) işini yürütür.
    bool ISplitEditActions.CanGoPrevious => StateService?.CanGoPreviousGlobal ?? false;
    bool ISplitEditActions.CanGoNext     => StateService?.CanGoNextGlobal ?? false;

    Task ISplitEditActions.GoPreviousAsync() => StateService?.GoPreviousGlobalAsync() ?? Task.CompletedTask;
    Task ISplitEditActions.GoNextAsync()     => StateService?.GoNextGlobalAsync()     ?? Task.CompletedTask;

    // Form owner'ı (CrudEditShell) her render'ında çağırır. DevExpress @bind editör değişince
    // RenderForm'u barındıran CrudEditShell re-render olur (edit page DEĞİL) → bu yüzden sinyali
    // oradan alıyoruz. EditModel gerçekten değiştiyse toolbar'ı (split host) tazeleriz; json
    // karşılaştırması sayesinde NotifyChanged→re-render→NotifyInput döngüsü kendiliğinden kırılır.
    private string? _lastSeenJson;
    void ISplitEditActions.NotifyInput() => NotifyToolbarIfChanged();
    void ISplitEditActions.CommitUndoStep() => CommitUndoStep();

    // TEK GÜVENİLİR sinyal: DevExpress @bind editör değişimi (blur/OnInput) TextChanged EventCallback'i
    // çalıştırır → bunun receiver'ı BU edit page → Blazor otomatik StateHasChanged → OnAfterRender.
    // DevExpress EditContext.OnFieldChanged'i tetiklemez ve DOM onchange'i bubble ETMEZ; bu yüzden
    // ne EditContext ne DOM event işe yaradı. Burada her render'da EditModel'i serialize edip
    // değiştiyse toolbar'ı (split host) tazeliyoruz; json karşılaştırması döngüyü kırar.
    protected override void OnAfterRender(bool firstRender) => NotifyToolbarIfChanged();

    private void NotifyToolbarIfChanged()
    {
        var json = SerializeModel();
        if (json == _lastSeenJson) return;        // net değişiklik yok → döngü/gereksiz tetik yok
        // Undo geçmişi: bir önceki kararlı state'i bir adım olarak it (BindValueMode.OnLostFocus ile
        // setter blur'da çalıştığından bu, blur başına bir undo adımı demektir). DOM onchange'e güvenmez.
        if (!_suppressUndoCapture && _undoCurrent != null && _undoCurrent != json)
        {
            _undoStack.Push(_undoCurrent);
            _redoStack.Clear();
        }
        _undoCurrent = json;
        _lastSeenJson = json;
        SplitHost?.NotifyChanged();               // Kaydet/Undo/Redo/Reset aktiflik durumunu tazele
        if (IsDirty != _lastSyncedDirty) SyncTabHeader();   // clean↔dirty geçişi → tab/top-panel "*" tazele
    }

    /// <summary>Editör commit (blur/change) anında, değer gerçekten değiştiyse undo geçmişine bir adım ekler.</summary>
    private void CommitUndoStep()
    {
        if (_suppressUndoCapture) return;
        var json = SerializeModel();
        if (json == _undoCurrent) return;            // bu editörde net değişiklik yok
        if (_undoCurrent != null) _undoStack.Push(_undoCurrent);
        _redoStack.Clear();
        _undoCurrent = json;
    }
    /// <summary>Edit modunda Sil aktif mi? Varsayılan: yeni kayıt değilse. Alt sınıf özelleştirebilir
    /// (ör. Role: static rol silinemez).</summary>
    protected virtual bool CanDeleteRecord => !IsNewMode;
    bool ISplitEditActions.CanDelete => !IsReadOnly && CanDeleteRecord;
    Task ISplitEditActions.DeleteAsync() => DeleteRecordAsync();

    [Inject]
    protected ITradeXpressUiService UiService { get; set; } = default!;

    [Inject]
    protected IEntityChangeNotifier EntityChanges { get; set; } = default!;

    [Inject]
    protected ILogger<CrudEditComponentBase<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto>> Logger { get; set; } = default!;

    [Inject]
    protected IPopupService PopupService { get; set; } = default!;

    protected abstract ICrudAppService<TGetDto, TListDto, TKey, TListRequestDto, TCreateDto, TUpdateDto> CrudAppService { get; }
    protected abstract string EntityChangeKey { get; }

    /// <summary>
    /// UI formunun doğrudan bağlandığı (bind edildiği) DTO.
    /// Yeni kayıt ise new(), düzenleme ise GetAsync ile dolar.
    /// </summary>
    protected TGetDto EditModel { get; set; } = new();

    protected bool IsNewMode => Id == null || Id.Equals(default(TKey));
    protected bool IsBusy { get; private set; }

    // ── Inline validation (alan-altı hata) altyapısı ──
    // EditContext, EditModel'e bağlanır; ValidateInput ihlalleri ValidationMessageStore'a yazar.
    // Sayfalar formu <CascadingValue Value="EditContext"> ile sarar → DevExpress editörleri mesajı
    // alanın altında gösterir. EditModel referansı değişince (yükleme/yeni) yeniden kurulur.
    protected EditContext? EditContext { get; private set; }
    private ValidationMessageStore? _messages;

    private void RebuildEditContext()
    {
        if (EditContext != null)
            EditContext.OnFieldChanged -= OnEditFieldChanged;
        EditContext = new EditContext(EditModel!);
        EditContext.OnFieldChanged += OnEditFieldChanged;
        _messages = new ValidationMessageStore(EditContext);
    }

    // Form alanı değişince undo geçmişini FIELD BAZLI tut: aynı alanda ardışık keystroke tek adım,
    // başka alana geçilince (≈ blur/OnLeave) önceki alanın son hali undo'ya itilir → yeni adım.
    private string? _activeUndoField;
    private void OnEditFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        if (!_suppressUndoCapture)
        {
            var fieldName = e.FieldIdentifier.FieldName;
            if (_activeUndoField != fieldName)
            {
                // Yeni alana geçildi → mevcut state'i (önceki alanın sonu) bir undo adımı olarak it.
                if (_undoCurrent != null) _undoStack.Push(_undoCurrent);
                _redoStack.Clear();
                _activeUndoField = fieldName;
            }
            // Aynı alanda ardışık değişiklik: yalnız current'i güncelle (yeni undo adımı yok).
            _undoCurrent = SerializeModel();
        }
        SplitHost?.NotifyChanged();
    }

    // ── Undo / Redo (snapshot tabanlı) ──
    private readonly System.Collections.Generic.Stack<string> _undoStack = new();
    private readonly System.Collections.Generic.Stack<string> _redoStack = new();
    private string? _undoCurrent;        // o anki form state (son snapshot)
    private bool _suppressUndoCapture;   // undo/redo/yükleme sırasında yeni snapshot alma

    private string? SerializeModel()
    {
        try { return System.Text.Json.JsonSerializer.Serialize(EditModel); }
        catch { return null; }
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        if (_undoCurrent != null) _redoStack.Push(_undoCurrent);
        _undoCurrent = _undoStack.Pop();
        _lastSeenJson = _undoCurrent;   // OnAfterRender bu değişimi yeni undo adımı sanmasın
        ApplySnapshot(_undoCurrent);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        if (_undoCurrent != null) _undoStack.Push(_undoCurrent);
        _undoCurrent = _redoStack.Pop();
        _lastSeenJson = _undoCurrent;
        ApplySnapshot(_undoCurrent);
    }

    private void ApplySnapshot(string? json)
    {
        if (json == null) return;
        try
        {
            var model = System.Text.Json.JsonSerializer.Deserialize<TGetDto>(json);
            if (model != null)
            {
                EditModel = model;
                _activeUndoField = null;   // undo/redo sonrası sonraki değişiklik yeni adım olsun
                _suppressUndoCapture = true;
                RebuildEditContext();   // referans değişimi field-change üretmez; yine de guard
                _suppressUndoCapture = false;
            }
        }
        catch { /* bozuk snapshot → yoksay */ }
        SplitHost?.NotifyChanged();
        StateHasChanged();   // EditModel yeni referans → formu (RenderForm) yeni değerlerle yeniden çiz
    }

    /// <summary>Kaydedilmemiş değişiklikleri at, kaydı orijinaline yeniden yükle.</summary>
    public virtual Task ResetAsync() => LoadDataAsync();

    // ── Dirty takibi ──
    // EditModel yüklendiğinde/kaydedildiğinde JSON anlık görüntüsü alınır; o andan beri değişti mi
    // diye karşılaştırılır. Serileştirme başarısızsa fail-open (IsDirty=true) → kaydetme engellenmez.
    private string? _cleanSnapshot;

    private void CaptureSnapshot()
    {
        try { _cleanSnapshot = System.Text.Json.JsonSerializer.Serialize(EditModel); }
        catch { _cleanSnapshot = null; }
        // Temiz state (yükleme/kayıt) → undo/redo geçmişini sıfırla, current'i bu state yap.
        _undoStack.Clear();
        _redoStack.Clear();
        _undoCurrent = _cleanSnapshot;
        _lastSeenJson = _cleanSnapshot;   // yükleme/kayıt sonrası ilk OnAfterRender yanlış adım itmesin
        _activeUndoField = null;
    }

    /// <summary>EditModel son yükleme/kayıttan beri değişti mi? (kaydet butonu vb. için)</summary>
    public bool IsDirty
    {
        get
        {
            if (HasExtraChanges) return true;   // EditModel dışı dirty kaynağı (ör. embedded izin paneli)
            if (_cleanSnapshot == null) return true;
            try { return System.Text.Json.JsonSerializer.Serialize(EditModel) != _cleanSnapshot; }
            catch { return true; }
        }
    }

    /// <summary>EditModel'e yansımayan dirty kaynağı — alt sayfalar embedded editör (izin paneli vb.) için override eder.</summary>
    protected virtual bool HasExtraChanges => false;

    protected override async Task OnInitializedAsync()
    {
        // Popup/standalone: köprüyü bu edit'e bağla. "Neredeyiz" canlı Id'den (CurrentKeyProvider), ayrılma
        // onayı dirty guard'dan (CanLeaveGuard), hedef kayda geçiş forma yüklemeden (OnRecordActivated).
        // Split'te SplitHost devrede; köprü bağlanmaz (StateService.GoGlobal split panelinden tetiklenmez).
        if (SplitHost == null && StateService != null)
        {
            StateService.CurrentKeyProvider = () => Id;
            StateService.CanLeaveGuard      = ConfirmCloseAsync;
            StateService.OnRecordActivated  = async t => { Id = (TKey)t.Key; await LoadDataAsync(); };
        }
        RebuildEditContext();   // ilk render güvenli (boş model)
        await LoadDataAsync();
        // Kapatma guard'ı — embedded panelde kapatma yok, guard kurulmaz.
        if (!IsEmbedded)
        {
            if (IsPopupMode && PopupService != null)
                PopupService.CloseGuard = ConfirmCloseAsync;
            else if (CurrentMdiTab != null)
                CurrentMdiTab.CanCloseAsync = ConfirmCloseAsync;
        }
        SplitHost?.RegisterEdit(this);
        await base.OnInitializedAsync();
    }

    public void Dispose()
    {
        if (EditContext != null)
            EditContext.OnFieldChanged -= OnEditFieldChanged;
        SplitHost?.UnregisterEdit(this);
        // Köprü delegeleri bu edit'i (closure ile) yakalar; StateService scoped → popup'tan uzun yaşar.
        // Kapanışta temizle ki disposed edit'e referans (stale closure) kalmasın. Split'te bağlanmadı.
        if (SplitHost == null && StateService != null)
        {
            StateService.CurrentKeyProvider = null;
            StateService.CanLeaveGuard      = null;
            StateService.OnRecordActivated  = null;
        }
    }

    /// <summary>
    /// Ayrılma onayı: dirty değilse serbest. Dirty ise "Kaydet / Yoksay / (çarpı=İptal)" sorar.
    /// Kaydet → kaydet, başarılıysa devam; Yoksay → değişiklikleri at, devam; İptal(çarpı) → kal.
    /// Dönüş: true = ayrılmaya/geçişe izin, false = iptal (mevcut kayıtta kal).
    /// </summary>
    protected virtual async Task<bool> ConfirmCloseAsync()
    {
        if (!IsDirty) return true;
        var result = await UiService.ConfirmAsync(
            L["UnsavedChangesConfirmation"].Value,
            title: null,
            yesText: L["SaveChanges"].Value,       // Değişiklikleri Kaydet
            noText: L["DiscardChanges"].Value,     // Değişiklikleri Yoksay
            showCancel: false,                     // İptal butonu yok; çarpı = iptal
            defaultYes: true);                     // "Değişiklikleri Kaydet" varsayılan (primary + odaklı)
        return result switch
        {
            ConfirmDialogResult.Yes => await SaveAsync(),  // kaydet; başarısızsa false → kal
            ConfirmDialogResult.No  => true,               // yoksay → geç
            _                       => false,              // çarpı/Cancel → kal
        };
    }

    public virtual async Task LoadDataAsync()
    {
        try
        {
            IsBusy = true;
            if (!IsNewMode && Id != null)
            {
                EditModel = await CrudAppService.GetAsync(Id);
            }
            else
            {
                EditModel = new TGetDto();
                await OnModelCreatedAsync(EditModel);
            }
            await OnModelLoadedAsync(EditModel);   // hem yeni hem mevcut, snapshot'tan ÖNCE → burada set edilen dirty saymaz
            CaptureSnapshot();
            RebuildEditContext();   // EditModel referansı değişti → context'i yenile
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading data for Edit form");
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
            SyncNavigationSelection();    // popup/standalone gezinme: açık kaydı StateService seçili öğesine hizala
            SplitHost?.NotifyChanged();   // yükleme sonrası dirty=false / IsNew değişti → toolbar tazele
            SyncTabHeader();              // model yüklendi (L2 kod + IsNew biliniyor) → MDI tab/top-panel başlığını kur
            StateHasChanged();            // EditModel yeni referans (Prev/Next/Reset) → formu yeniden çiz
        }
    }

    /// <summary>Standalone/popup edit'te Previous/Next için: ListView'da satıra tıklayıp popup açmak
    /// selection'ı set etmez (yalnız checkbox seçer). Bu yüzden açık kaydı (Id) merkezi StateService'in
    /// SelectedItem'ına hizalarız ki CanGoPrevious/CanGoNext doğru hesaplansın. Split'te SplitHost devrede.</summary>
    private void SyncNavigationSelection()
    {
        if (SplitHost != null || StateService == null || IsNewMode || Id == null) return;
        var match = System.Linq.Enumerable.FirstOrDefault(
            StateService.ListDataSource, x => x != null && Equals(x.Id, Id));
        // Not: "neredeyiz" artık köprünün CurrentKeyProvider'ından (()=>Id) canlı gelir; burada yalnız liste
        // grid'inin highlight'ı için açık kayıt SelectedItem'a hizalanır (ilk açılış / Prev/Next sonrası).
        if (match != null && !ReferenceEquals(StateService.SelectedItem, match))
            StateService.SelectedItem = match;
    }

    /// <summary>
    /// Yeni kayıt oluşturulduğunda DTO'nun default değerlerini set etmek için ezilebilir.
    /// </summary>
    protected virtual Task OnModelCreatedAsync(TGetDto model) => Task.CompletedTask;

    /// <summary>Model yüklendikten (yeni veya mevcut) SONRA, temiz-snapshot ALINMADAN ÖNCE çağrılır. Burada
    /// yapılan değişiklikler "kirli" sayılmaz (örn. bağlama göre varsayılan alan doldurma, ek veri yükleme).</summary>
    protected virtual Task OnModelLoadedAsync(TGetDto model) => Task.CompletedTask;

    /// <summary>
    /// XAF Validation modülü emsali: Create/UpdateDto üzerindeki DataAnnotation'ları (Required,
    /// StringLength/MaxLength, Range) sunucuya gitmeden kontrol eder. Mesajlar projenin
    /// "Validation:*" anahtarlarıyla LOCALIZE edilir; alan adı için L[propName] denenir (yoksa ham ad).
    /// Geçersizse toast gösterir ve false döner. Özel kurallar için override edilebilir.
    /// </summary>
    protected virtual bool ValidateInput(object input)
    {
        // (alanAdı, mesaj) — alan adı DTO property'si (EditModel ile aynı) → inline eşleşir.
        var found = new List<(string Field, string Message)>();
        foreach (var prop in input.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var value = prop.GetValue(input);
            var field = FieldDisplayName(prop.Name);

            foreach (var attr in prop.GetCustomAttributes(inherit: true))
            {
                switch (attr)
                {
                    case RequiredAttribute when value == null || (value is string s && string.IsNullOrWhiteSpace(s)):
                        found.Add((prop.Name, L["Validation:Required", field].Value));
                        break;
                    case StringLengthAttribute sl when value is string s2 && s2.Length > sl.MaximumLength:
                        found.Add((prop.Name, L["Validation:MaxLength", field, sl.MaximumLength].Value));
                        break;
                    case MaxLengthAttribute ml when ml.Length >= 0 && value is string s3 && s3.Length > ml.Length:
                        found.Add((prop.Name, L["Validation:MaxLength", field, ml.Length].Value));
                        break;
                    case RangeAttribute r when value is IComparable cmp:
                        try
                        {
                            var min = (IComparable)Convert.ChangeType(r.Minimum, value!.GetType());
                            var max = (IComparable)Convert.ChangeType(r.Maximum, value.GetType());
                            if (cmp.CompareTo(min) < 0 || cmp.CompareTo(max) > 0)
                                found.Add((prop.Name, L["Validation:Range", field, r.Minimum, r.Maximum].Value));
                        }
                        catch { /* tür uyuşmazsa atla */ }
                        break;
                }
            }
        }

        // Inline mesajları her zaman tazele (geçerliyse temizlenmiş olur).
        _messages?.Clear();
        var distinct = found.Distinct().ToList();
        if (EditContext != null && _messages != null)
        {
            foreach (var (f, m) in distinct)
                _messages.Add(EditContext.Field(f), m);   // aynı alanda çok mesaj → alt alta gösterilir
            EditContext.NotifyValidationStateChanged();
        }

        if (distinct.Count == 0) return true;
        // Toast: her hata AYRI notification (XAF tarzı).
        foreach (var msg in distinct.Select(x => x.Message).Distinct())
            UiService.ShowErrorToast(msg);
        return false;
    }

    /// <summary>
    /// Hatayı işler: kullanıcı-dostu mesaj varsa (validation / iş kuralı) toast'ta gösterir; YOKSA
    /// (teknik hata) genel mesaj gösterir ve TAM detayı (<see cref="IClientErrorReporter"/>) geliştirici
    /// tanılama yüzeyine (Developer Error Panel) iletir — Blazor Server'da ILogger tarayıcıya gitmez.
    /// </summary>
    protected void ShowError(Exception ex)
    {
        // BusinessException/IHasErrorCode in-process'te (Blazor Server) Message lokalize olmaz → kodu elle
        // çevir; sonra validation/remote için formatter; ikisi de yoksa teknik hata → genel mesaj + panele detay.
        var friendly = LocalizeErrorCode(ex) ?? CrudErrorFormatter.Extract(ex);
        if (friendly is null)
        {
            ServiceProvider.GetService<IClientErrorReporter>()?.Report(ex.Message, ex.ToString());
        }

        ShowErrorLines(friendly ?? L["UnexpectedError"]);
    }

    /// <summary>
    /// Hata kodlu (<see cref="Volo.Abp.BusinessException"/>) exception'ın kodunu, kod-namespace eşlemesinden
    /// (MapCodeNamespace) bulunan kaynakla lokalize eder. In-process (Blazor Server) çağrıda ABP'nin
    /// HTTP pipeline lokalizasyonu çalışmadığından gerekir. Eşleşme/çeviri yoksa null.
    /// </summary>
    private string? LocalizeErrorCode(Exception ex)
    {
        for (var cur = ex; cur != null; cur = cur.InnerException)
        {
            if (cur is not Volo.Abp.BusinessException { Code: { } code } || !code.Contains(':'))
            {
                continue;
            }

            var ns = code.Substring(0, code.IndexOf(':'));
            var mappings = ServiceProvider
                .GetService<Microsoft.Extensions.Options.IOptions<Volo.Abp.Localization.ExceptionHandling.AbpExceptionLocalizationOptions>>()
                ?.Value.ErrorCodeNamespaceMappings;
            if (mappings is null || !mappings.TryGetValue(ns, out var resourceType))
            {
                continue;
            }

            var localized = ServiceProvider
                .GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizerFactory>()
                .Create(resourceType)[code];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        return null;
    }

    /// <summary>Çok satırlı hata metnini satır başına ayrı toast olarak gösterir (XAF tarzı).</summary>
    protected void ShowErrorLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) { UiService.ShowErrorToast(text); return; }
        foreach (var line in lines) UiService.ShowErrorToast(line);
    }

    /// <summary>Alan adı için yerelleştirilmiş başlık (L[propName]); anahtar yoksa ham ad.</summary>
    private string FieldDisplayName(string propName)
    {
        var loc = L[propName];
        return loc.ResourceNotFound ? propName : loc.Value;
    }

    public virtual async Task<bool> SaveAsync()
    {
        var wasNew = IsNewMode;   // notify anında Id set edilmiş olacağından önceden yakala
        try
        {
            IsBusy = true;

            if (IsNewMode)
            {
                var createDto = ObjectMapper.Map<TGetDto, TCreateDto>(EditModel);
                if (!ValidateInput(createDto)) return false;   // XAF tarzı: geçersizse sunucuya gitmeden engelle
                var created = await CrudAppService.CreateAsync(createDto);
                Id = created.Id;

                // Kaydedildikten sonra EditModel'in ID'sini de güncelle.
                if (EditModel is Volo.Abp.Application.Dtos.EntityDto<TKey> dto)
                {
                    dto.Id = created.Id;
                }
            }
            else
            {
                var updateDto = ObjectMapper.Map<TGetDto, TUpdateDto>(EditModel);
                if (!ValidateInput(updateDto)) return false;
                await CrudAppService.UpdateAsync(Id!, updateDto);
            }

            EntityChanges.Notify(EntityChangeKey,
                wasNew ? EntityChangeKind.Created : EntityChangeKind.Updated, Id);
            CaptureSnapshot(); // kayıt sonrası model artık "temiz"
            SyncTabHeader();   // dirty=false + (yeni→mevcut) → tab/top-panel başlığı + "*" temizliği (json değişmeyebilir, elle)
            UiService.ShowSuccessToast(LocalizationResource != null ? L["SuccessfullySaved"].Value : "Successfully saved");

            if (OnSaved.HasDelegate)
            {
                await OnSaved.InvokeAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error saving data in Edit form");
            // API'den gelen mesajları göster
            ShowError(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
            SplitHost?.NotifyChanged();   // kayıt sonrası dirty=false → birleşik toolbar Kaydet'i pasifleşir
        }
    }

    public virtual async Task SaveAndNewAsync()
    {
        if (!await SaveAsync()) return;
        if (IsEmbedded)
        {
            // Split panel: edit'in Id'sini elle sıfırlamak parent'ın eski Id'siyle ezilir;
            // bunun yerine host yeni-kayıt durumuna geçer → edit @key ile remount olup boş yüklenir.
            if (SplitHost != null) await SplitHost.RequestNewAsync();
        }
        else
        {
            Id = default;
            await LoadDataAsync();
        }
    }

    public virtual async Task SaveAndCloseAsync()
    {
        if (!await SaveAsync()) return;
        if (IsEmbedded)
        {
            // Split panel: kaydedildi (artık temiz) → guard'ı geçip listeye dön.
            if (SplitHost != null) await SplitHost.RequestCloseAsync();
        }
        else
        {
            await CloseAsync();
        }
    }

    public virtual async Task DeleteRecordAsync()
    {
        if (IsNewMode || Id == null) return;
        var confirm = await UiService.ConfirmDeleteAsync(L["DeleteConfirmationMessage"].Value ?? "Are you sure?");
        if (confirm != ConfirmDialogResult.Yes) return;
        try
        {
            IsBusy = true;
            var deletedId = Id;
            await CrudAppService.DeleteAsync(Id);
            EntityChanges.Notify(EntityChangeKey, EntityChangeKind.Deleted, deletedId);
            UiService.ShowSuccessToast(L["SuccessfullyDeleted"].Value ?? "Deleted");
            if (OnDeleted.HasDelegate)
                await OnDeleted.InvokeAsync();
            await CloseAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error deleting data");
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public virtual async Task CloseAsync()
    {
        if (IsEmbedded) return;   // panel kapatılamaz; SplitCrudView seçimi sıfırlar
        if (OnClosed.HasDelegate)
            await OnClosed.InvokeAsync();
    }
}
