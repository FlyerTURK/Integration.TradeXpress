using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// <c>DrillList.razor</c> code-behind'ı — @code bloğu MEKANİK olarak buraya taşındı (ui-blazor:
/// .razor'da @code yasak). Markup içeren üç Razor inline template (SearchBoxTemplate /
/// ActiveFilterTemplate / EditFormBody) C# dosyasında yazılamadığından alan olarak burada durur,
/// .razor'ın en üstündeki kod bloğunda her render başında atanır. Davranış değişikliği YOK.
/// </summary>
public partial class DrillList<TItem> where TItem : class
{
    // Pencere durumu (fullscreen/minimize) + Toggle metotları + butonlar ARTIK EditShell'de.
    // DrillList yalnız _popupVisible + kapanış guard'ını (OnPopupClosing/OnPopupClosed) tutar.

    [Inject] private IUiInteractionService UiService { get; set; } = default!;
    [Inject] private IGridExportAssemblyLoader ExportLoader { get; set; } = default!;
    [Inject] private IUiStateService UiStateService { get; set; } = default!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    // Persist (create/update/delete) hataları → LOKALİZE dostu toast. BusinessException in-process (Blazor Server)
    // Message'ı lokalize etmez; CrudErrorPresenter kod-namespace eşlemesiyle çevirir (çeviremezse genel mesaj).
    private void ShowExceptionToast(Exception ex)
    {
        UiService.ShowErrorToast(CrudErrorPresenter.ToFriendlyMessage(ex, ServiceProvider) ?? L["UnexpectedError"].Value);
    }

    // Markup içeren şablonlar — .razor'ın üstündeki kod bloğunda atanır (kullanım BuildListActions +
    // popup gövdesi). Razor inline template yalnız .razor içinde yazılabilir; alan tanımı burada.
    private RenderFragment<IToolbarItemInfo> SearchBoxTemplate = default!;
    private RenderFragment<IToolbarItemInfo> ActiveFilterTemplate = default!;
    private RenderFragment EditFormBody = default!;

    [Parameter] public string? Title { get; set; }
    [Parameter] public string? EntityName { get; set; }

    /// <summary>Bu entity'nin ikonu (FontAwesome class) — edit popup başlığında. Boşsa generic ikon.</summary>
    [Parameter] public string? EntityIcon { get; set; }

    /// <summary>Edit başlığında gösterilecek birincil değer seçici (genelde Code).
    /// Yeni → "Yeni {Entity} {Code?}", Düzenle → "{Entity} {Code}".</summary>
    [Parameter] public Func<TItem, string?>? PrimaryTextSelector { get; set; }

    /// <summary>Varsa üst (parent) entity adı — yalnız AÇILAN EDIT POPUP başlığına " - [Parent: Text]"
    /// eklenir (grid/liste alanında gösterilmez).</summary>
    [Parameter] public string? ParentEntityName { get; set; }

    /// <summary>Üst entity'nin gösterim metni (genelde Code). ParentEntityName ile birlikte.</summary>
    [Parameter] public string? ParentText { get; set; }

    [Parameter, EditorRequired] public List<TItem> Items { get; set; } = default!;
    [Parameter] public string KeyFieldName { get; set; } = "ClientKey";

    /// <summary>Düzenleme popup'ının azami genişliği. Varsayılan 960px (2026-07-28 Hakan: 720px sekmeli/çok
    /// alanlı formlarda dardı — alanlar üst üste biniyor, satır başına iki kontrol bile sığmıyordu). Dar bir
    /// forma sahip drill'ler bunu düşürerek eski görünümü koruyabilir.</summary>
    [Parameter] public string EditPopupMaxWidth { get; set; } = "960px";
    [Parameter] public int PageSize { get; set; } = 10;

    /// <summary>Sayfalayıcı görünsün mü — <b>VARSAYILAN KAPALI</b> (2026-07-27 Hakan kararı).
    /// Drill listeleri form İÇİ listelerdir (kalemler, varyantlar, şubeler): sayfalara bölünince ikinci
    /// sayfadaki satırlar gözden kaçıyor ve eksik veri giriliyor. Uzun liste beklenen yerlerde
    /// <see cref="VirtualScrollingEnabled"/> ile birlikte kullanılır; gerçekten sayfalama istenen bir
    /// liste çıkarsa <c>true</c> geçilir.</summary>
    [Parameter] public bool PagerVisible { get; set; }

    /// <summary>Sayfa boyutu seçici (pager yanında). Uzun drill listelerinde kullanıcı sayfa başına kaç
    /// satır göreceğini seçer; kısa alt listelerde gürültü olduğu için varsayılan KAPALI.</summary>
    [Parameter] public bool PageSizeSelectorVisible { get; set; }

    /// <summary>Gruplama paneli (kolon başlığını sürükle → o kolona göre grupla). Varsayılan KAPALI —
    /// gerekçe ve "gruplanabilmenin şartı FieldName'dir" notu <c>TxGrid.ShowGroupPanel</c>'de.</summary>
    [Parameter] public bool ShowGroupPanel { get; set; }

    /// <summary>Grup satırının gövdesi — enum/bool kolonlarında ham değer yerine okunur metin yazmak için
    /// (bkz. <c>TxGrid.DataColumnGroupRowTemplate</c>). Verilmezse DevExpress varsayılanı çizilir.</summary>
    [Parameter] public RenderFragment<GridDataColumnGroupRowTemplateContext>? DataColumnGroupRowTemplate { get; set; }

    /// <summary>Sanal kaydırma — sayfalayıcı kapatıldığında uzun listeyi DOM'u şişirmeden gösterir
    /// (yalnız görünür satırlar çizilir).</summary>
    [Parameter] public bool VirtualScrollingEnabled { get; set; }
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Toolbar'da arama kutusu göster (genel listeleme formuyla parite). Varsayılan AÇIK; istenen drill
    /// <c>false</c> geçebilir. <see cref="SearchMode"/> ile in-grid (varsayılan) veya server-side davranır.</summary>
    [Parameter] public bool ShowSearch { get; set; } = true;

    /// <summary>Toolbar'da Dışa Aktar (Excel/PDF) göster. Varsayılan AÇIK; istenen drill <c>false</c> geçebilir.
    /// Export paylaşılan <see cref="IGridExportAssemblyLoader"/> + grid.ExportToXlsx/PdfSafeAsync ile (Server'da çalışır).</summary>
    [Parameter] public bool ShowExport { get; set; } = true;

    /// <summary>Arama modu. Varsayılan InGrid (in-memory drill grid'inin yüklü verisinde istemci filtresi).
    /// Persistent drill geldiğinde ServerSide verilip <see cref="OnServerSearch"/> bağlanır (sunucudan filtreli çekim).</summary>
    [Parameter] public GridSearchMode SearchMode { get; set; } = GridSearchMode.InGrid;

    /// <summary>ServerSide arama modunda arama metnini parent'a iletir (ileride persistent drill: sunucudan filtreli
    /// yeniden çek). InGrid modda kullanılmaz.</summary>
    [Parameter] public EventCallback<string> OnServerSearch { get; set; }

    /// <summary>true → satır-içi (hücre editörü) düzenleme: cell'ler DevExpress native EditRow
    /// editörlerine döner; New/Edit/Delete grid'in command column'undan gelir. Yalnız grid'de GÖRÜNEN
    /// kolonlar düzenlenir (form-only alanlar değil). İç içe (nested) DrillList'te KULLANMA — orada
    /// popup (varsayılan) daha güvenli. false → popup düzenleme (varsayılan).</summary>
    [Parameter] public bool AllowInlineEdit { get; set; }

    /// <summary>Grid kutusunun minimum yüksekliği. Varsayılan: tüm drill'lerde ortak
    /// <see cref="DrillConsts.DefaultGridHeight"/>. Gerekirse kullanım başına ezilebilir.</summary>
    [Parameter] public string MinHeight { get; set; } = DrillConsts.DefaultGridHeight;

    /// <summary>Grid kutusunun maksimum yüksekliği (yalnız MOBİL). Varsayılan: MinHeight ile EŞİT (sabit yükseklik).</summary>
    [Parameter] public string MaxHeight { get; set; } = DrillConsts.DefaultGridHeight;

    /// <summary>Masaüstü TABAN yüksekliği (içerik azsa bu kadar). Varsayılan 300px.</summary>
    [Parameter] public string DesktopMinHeight { get; set; } = "300px";

    /// <summary>Masaüstü TAVAN yüksekliği (vh-bağlı; içerik buraya kadar büyür, taşınca grid içi scroll). Varsayılan 62vh.
    /// vh-bağlı → parent'ın bounded-flex olmasına BAĞIMLI DEĞİL (popup/sekme/sayfa her yerde çalışır).</summary>
    [Parameter] public string DesktopMaxHeight { get; set; } = "62vh";

    // Responsive (DxLayoutBreakpoint MaxWidth=768): mobil = sabit min/max; masaüstü = vh-bağlı (min taban, max tavan).
    private bool _isMobile;

    [Parameter, EditorRequired] public RenderFragment GridColumns { get; set; } = default!;
    [Parameter, EditorRequired] public RenderFragment<TItem> EditContent { get; set; } = default!;

    /// <summary>Popup içinde, edit EditForm'unun DIŞINDA render edilen opsiyonel ek içerik (ör. ikinci
    /// seviye gömülü drill). EditForm dışı olduğundan iç içe EditContext NRE'si oluşturmaz — nested
    /// DrillList için güvenli barınak. Düzenlenen öğeyi (_editItem) context olarak alır.</summary>
    [Parameter] public RenderFragment<TItem>? AfterEditForm { get; set; }
    [Parameter, EditorRequired] public Func<TItem> NewItemFactory { get; set; } = default!;

    /// <summary>Bu sayının altına inilemez (en az N child kuralı). Sil engellenir.</summary>
    [Parameter] public int MinItems { get; set; }

    /// <summary>Bu sayının üstüne çıkılamaz (en fazla N child kuralı; ör. ürün başına 5 nitelik).
    /// Dolunca Yeni pasifleşir; null (varsayılan) = sınırsız.</summary>
    [Parameter] public int? MaxItems { get; set; }

    /// <summary>false → toolbar'dan Yeni + popup'tan "Kaydet ve Yeni" GİZLENİR (elle kayıt eklenemez;
    /// ör. varyantlar niteliklerden ÜRETİLİR). Düzenleme etkilenmez. Varsayılan true.</summary>
    [Parameter] public bool AllowAdd { get; set; } = true;

    /// <summary>false → toolbar + edit popup'tan Sil GİZLENİR (elle kayıt silinemez; ör. üretilen
    /// varyantlar senkronda temizlenir). Düzenleme etkilenmez. Varsayılan true.</summary>
    [Parameter] public bool AllowDelete { get; set; } = true;

    // Üst sınır dolu mu? Silinmiş işaretliler sayılmaz (FilterPredicate IsDeleted'ı gizliyor varsayımıyla
    // görünür sayı esas alınır — MinItems/DeleteSelected ile aynı bakış).
    private bool MaxItemsReached =>
        MaxItems is { } max && (FilterPredicate != null ? Items.Count(FilterPredicate) : Items.Count) >= max;

    /// <summary>Entity'ye özel silme engeli — engel varsa lokalize mesaj döner, yoksa null.</summary>
    [Parameter] public Func<TItem, string?>? DeleteGuard { get; set; }

    /// <summary>Entity'ye özel KAYDETME engeli (DeleteGuard'ın Save karşılığı) — mesaj dönerse uyarı toast'ı
    /// gösterilir, popup açık kalır (ör. aynı ürüne aynı görsel URL'si/dosya adı iki kez girilemez).</summary>
    [Parameter] public Func<TItem, string?>? SaveGuard { get; set; }

    /// <summary>Toolbar'a sayfaya özel ek aksiyonlar — descriptor liste, SortIndex'li (liste sayfalarıyla AYNI
    /// sözleşme; varsayılan SortIndex 300 = Sil ile Arama arası). Eski <c>RenderFragment ToolbarActions</c> kaldırıldı.</summary>
    [Parameter] public IReadOnlyList<CrudToolbarAction>? CustomActions { get; set; }

    /// <summary>Opsiyonel istemci-tarafı filtre. Verilirse DxGrid yalnız eşleşen öğeleri gösterir;
    /// Items (tam liste) mutation için dokunulmaz kalır.</summary>
    [Parameter] public Func<TItem, bool>? FilterPredicate { get; set; }

    /// <summary>Soft-delete (graf) modu: silme satırı LİSTEDEN ÇIKARMAZ — <see cref="MarkDeleted"/> ile
    /// işaretler (arka planda yaşar, parent save'inde IsDeleted olarak gider). Idsi olmayan (yeni) öğe
    /// silinince listeden TAMAMEN çıkar (<see cref="IsNewItem"/> ile ayırt edilir). Grid'de görünmemesi
    /// için ayrıca FilterPredicate ile IsDeleted olanlar gizlenmeli.</summary>
    [Parameter] public bool SoftDelete { get; set; }

    /// <summary>Öğe henüz kalıcı değil mi (Id boş)? Soft-delete'te yeni+silinen doğrudan listeden çıkar.</summary>
    [Parameter] public Func<TItem, bool>? IsNewItem { get; set; }

    /// <summary>Mevcut öğeyi silindi işaretle (IsDeleted=true). Soft-delete modunda kullanılır.</summary>
    [Parameter] public Action<TItem>? MarkDeleted { get; set; }

    // Her render'da YENİ liste → DxGrid Data referansı değişir → ekleme/silme/save-and-new anında yansır
    // (aksi halde aynı List referansında mutasyonu grid algılamaz; özellikle FilterPredicate'siz drill'lerde).
    private IEnumerable<TItem> FilteredItems
    {
        get
        {
            var q = FilterPredicate != null ? Items.Where(FilterPredicate) : Items;
            // IIsActive öğelerde Aktif/Pasif filtresi (switch açıkken yalnız aktifler) — genel liste ile aynı.
            if (ItemIsActiveAware && ShowActiveFilter && _showActiveOnly)
                q = q.Where(ItemIsActive);
            return q.ToList();
        }
    }

    /// <summary>Liste değişince parent'a haber verir (dirty işaretleme / yeniden render).</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>Yenile — verilirse toolbar'da Refresh butonu çıkar (genelde persistent modda parent
    /// öğeleri sunucudan yeniden çeker). NOT: Persistent kayıt için parent kaydının önceden
    /// kaydedilmiş (Id'li) olması gerekir; aksi halde PersistCreate/Update parent Id'sini bulamaz.</summary>
    [Parameter] public EventCallback OnRefresh { get; set; }

    /// <summary>Seçili öğe değişince parent'a bildirir (alt seviye drill action'ı için).</summary>
    [Parameter] public EventCallback<TItem?> OnSelectionChanged { get; set; }

    /// <summary>Bir öğe başarıyla kaydedilince parent'a bildirir — tekil-bayrak transferi (ör. HQ devri)
    /// için. Items'a yazıldıktan SONRA, popup kapanmadan ÖNCE tetiklenir; parent Items'taki diğer öğeleri
    /// bu callback içinde güvenle mutasyona uğratabilir.</summary>
    [Parameter] public EventCallback<TItem> OnItemSaved { get; set; }

    /// <summary>Bir öğe silinince (listeden çıkmadan ÖNCE) parent'a bildirir — silinen sunucu Id'sini
    /// kaydetmek için (in-memory commit'te SaveTree'ye DeletedXIds olarak gider).</summary>
    [Parameter] public EventCallback<TItem> OnItemDeleted { get; set; }

    /// <summary>Düzenlemede canlı nesne yerine kopya üzerinde çalışmak için (Cancel geri alabilsin).
    /// Verilmezse canlı nesne düzenlenir (Cancel geri almaz).</summary>
    [Parameter] public Func<TItem, TItem>? CloneFactory { get; set; }

    // Persistent mod — verilirse her işlem anında kalıcı yazılır; verilmezse saf in-memory.
    [Parameter] public Func<TItem, Task<TItem>>? PersistCreate { get; set; }
    [Parameter] public Func<TItem, Task<TItem>>? PersistUpdate { get; set; }
    [Parameter] public Func<TItem, Task>? PersistDelete { get; set; }

    private bool Persistent => PersistCreate != null;

    private IReadOnlyList<object>? _selectedItems;

    // Grid referansı (export için) + in-grid arama metni. Hangi TxGrid render edilirse (inline/normal)
    // _txGrid ona @ref olur (aynı anda yalnız biri); _grid = aktif TxGrid'in InnerGrid'i (IGrid, export vb.).
    private TxGrid? _txGrid;
    private DevExpress.Blazor.IGrid? _grid => _txGrid?.InnerGrid;
    private string? _searchText;

    // Tek seçili öğe — Edit butonu ve popup için kullanılır.
    private TItem? SelectedItem => _selectedItems?.Count == 1 ? _selectedItems[0] as TItem : null;

    // Sil butonu aktifliği: seçim var + seçilenlerin hiçbiri DeleteGuard ile engellenmiyor (ör. varsayılan kasa).
    private bool CanDeleteSelection =>
        _selectedItems is { Count: > 0 }
        && (DeleteGuard == null || _selectedItems.OfType<TItem>().All(i => DeleteGuard(i) == null));

    // ── Liste toolbar (genel CrudToolbar/EditToolbar ile AYNI ToolbarRenderer; ikiz değil) ──
    private static readonly bool ItemIsActiveAware =
        typeof(Integration.Framework.Base.Dtos.Interfaces.IIsActive).IsAssignableFrom(typeof(TItem));
    // IIsActive MARKER'dır (property YOK) → IsActive'i reflection ile oku (tip başına bir kez cache).
    private static readonly System.Reflection.PropertyInfo? IsActiveProp =
        ItemIsActiveAware ? typeof(TItem).GetProperty("IsActive") : null;
    private static bool ItemIsActive(TItem item) => IsActiveProp?.GetValue(item) as bool? ?? true;

    /// <summary>IIsActive öğelerde Aktif/Pasif filtre switch'i göster (AKSİ belirtilmedikçe). false → gizle.</summary>
    [Parameter] public bool ShowActiveFilter { get; set; } = true;
    private bool _showActiveOnly = true;   // varsayılan: yalnız aktif (genel liste ile aynı)
    private void OnActiveFilterChanged(bool v) { _showActiveOnly = v; StateHasChanged(); }

    private IReadOnlyList<CrudToolbarAction> ListToolbarActions =>
        BuildListActions().Where(a => a.Visible)
            .OrderBy(a => a.Alignment == ToolbarItemAlignment.Right ? 1 : 0)
            .ThenBy(a => a.SortIndex).ToList();

    // Kimlik merkezî CrudToolbarActions kataloğundan (CrudToolbar/EditToolbar ile AYNI); burada yalnız in-memory
    // drill'e özgü Visible/Enabled/OnClick. Yeni/Sil yalnız popup modda toolbar'dan (AllowInlineEdit'te grid command column).
    private List<CrudToolbarAction> BuildListActions()
    {
        var list = new List<CrudToolbarAction>
        {
            CrudToolbarActions.New(L, !ReadOnly && !AllowInlineEdit && AllowAdd, !_busy && !MaxItemsReached, () => { StartNew(); return Task.CompletedTask; }),
            CrudToolbarActions.Delete(L, !ReadOnly && !AllowInlineEdit && AllowDelete, CanDeleteSelection && !_busy, DeleteSelected),
            // Dışa Aktar öncesi yer tutucu — arama ikilisi aşağıda merkezî fabrikadan eklenir.
            // Dışa Aktar (opt-in) — Excel/PDF, paylaşılan export loader.
            CrudToolbarActions.Export(L, ShowExport, DoExportExcel, DoExportPdf),
            // Yenile — yalnız OnRefresh verilmişse (persistent drill).
            CrudToolbarActions.Refresh(L, OnRefresh.HasDelegate, !_busy, () => OnRefresh.InvokeAsync()),
            // IsActive filtre switch (IIsActive ise + ShowActiveFilter).
            CrudToolbarActions.ActiveFilter(ItemIsActiveAware && ShowActiveFilter, ActiveFilterTemplate),
        };

        // ARAMA: dar ekranda kutu yerine İKON — karar CrudToolbar ile AYNI merkezî fabrikada (eşik tek yerde).
        // İkon, grid'in gömülü arama kutusunu açıp kapatır (liste toolbar'ındaki davranışın aynısı).
        // Dar-ekran ölçütü DrillList'in KENDİ DxLayoutBreakpoint'i (_isMobile) — responsive yüksekliği de o
        // sürüyor. Ayrı bir cascade eklemek ikinci bir eşik doğururdu; bileşen tek ölçüte bakmalı.
        list.AddRange(CrudToolbarActions.Search(L, ShowSearch, _isMobile, SearchBoxTemplate, ToggleGridSearchAsync));

        // Sayfaya özel custom action'lar — liste sayfalarıyla AYNI sözleşme (descriptor; SortIndex'leri çağıran belirler, varsayılan 300).
        if (CustomActions != null)
            list.AddRange(CustomActions);

        return list;
    }

    /// <summary>Dar ekranda arama ikonunun açıp kapattığı GÖMÜLÜ grid arama kutusu — liste toolbar'ındaki
    /// (<c>CrudLayout.ToggleGridSearch</c>) davranışın aynısı. Kutu toolbar'da yer kaplamaz, grid'in içinde açılır.</summary>
    private bool _showGridSearch;

    private Task ToggleGridSearchAsync()
    {
        _showGridSearch = !_showGridSearch;
        return InvokeAsync(StateHasChanged);
    }

    private async Task OnDrillSearch(string text)
    {
        if (SearchMode == GridSearchMode.ServerSide)
        {
            await OnServerSearch.InvokeAsync(text);   // ileride persistent drill: parent sunucudan filtreli çeker
            return;
        }
        _searchText = text;   // InGrid: grid yüklü veride filtreler
        StateHasChanged();
    }

    private async Task DoExportExcel()
    {
        await ExportLoader.EnsureLoadedAsync();
        await _grid.ExportToXlsxSafeAsync("Export");
    }

    private async Task DoExportPdf()
    {
        await ExportLoader.EnsureLoadedAsync();
        await _grid.ExportToPdfSafeAsync("Export");
    }

    private TItem? _editItem;
    private TItem? _editOriginal;  // düzenlenen orijinal liste öğesi (clone modunda geri yazma için)
    private bool _isNew;
    private bool _popupVisible;
    private bool _pendingEditRender;   // popup açılışında combo/lookup'lar seçili değeri göstersin diye tek ek render
    private bool _busy;
    private EditContext? _editContext;
    private string? _editSnapshot;  // açılış anındaki _editItem JSON'u → dirty karşılaştırması (ana edit formuyla aynı)

    // Kaydedilmemiş değişiklik var mı? Mevcut _editItem JSON'u açılış snapshot'ından farklıysa dirty.
    // Serileştirme başarısızsa fail-open (dirty=true → kaydetme/onay engellenmesin).
    private bool IsEditDirty
    {
        get
        {
            if (_editItem == null) return false;
            if (_editSnapshot == null) return true;
            try { return System.Text.Json.JsonSerializer.Serialize(_editItem) != _editSnapshot; }
            catch { return true; }
        }
    }

    // Alan değişince başlık "*" + Save aktifliği canlı güncellensin (DevExpress inputlar EditContext'e bildirir).
    private void OnEditFieldChanged(object? sender, FieldChangedEventArgs e) => StateHasChanged();

    private void SetEditContext(TItem item)
    {
        if (_editContext != null) _editContext.OnFieldChanged -= OnEditFieldChanged;
        _editContext = new EditContext(item);
        _editContext.OnFieldChanged += OnEditFieldChanged;
        try { _editSnapshot = System.Text.Json.JsonSerializer.Serialize(item); }
        catch { _editSnapshot = null; }
    }

    private void ClearEditContext()
    {
        if (_editContext != null) _editContext.OnFieldChanged -= OnEditFieldChanged;
        _editContext = null;
        _editSnapshot = null;
    }

    // Stale seçim emniyeti (framework düzeyi): parent re-render'da Items değiştiyse (ör. clone-swap:
    // Items[idx]=clone → eski referans listede yok), referansı kalmayan seçimleri temizle.
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (Items != null && _selectedItems != null)
        {
            var hasStale = _selectedItems.OfType<TItem>().Any(s => !Items.Contains(s));
            if (hasStale) _selectedItems = null;
        }
    }

    // Drill popup açılışında DxComboBox/lookup'lar önceden YÜKLÜ seçili değeri ilk render'da göstermeyebilir
    // (standalone'da model async yüklendiği için iki-fazlı render bunu örtüyor). Popup açılınca BİR ek render
    // tetikle → Value↔Data eşleşmesi yeniden değerlenir. Flag ile tek sefer (render loop yok).
    protected override void OnAfterRender(bool firstRender)
    {
        if (_pendingEditRender)
        {
            _pendingEditRender = false;
            StateHasChanged();
        }
    }

    private async Task OnGridSelectionChanged(IReadOnlyList<object> items)
    {
        _selectedItems = items;
        await OnSelectionChanged.InvokeAsync(SelectedItem);
    }

    // Edit popup YAPISAL başlığı — genel popup chrome ile AYNI engine (TabHeaderData → EditHeaderView):
    // L1 (Yeni) tür · L2 kayıt değeri (primary) · L3 parent. Dirty "*" EditHeaderView'de stillenir.
    private TabHeaderData BuildDrillHeader() => new()
    {
        NewPrefix = _isNew ? L["New"].Value : null,
        FormCaption = EntityName ?? string.Empty,
        EntityValue = (_editItem != null && PrimaryTextSelector != null) ? PrimaryTextSelector(_editItem) : null,
        ParentLabel = ParentEntityName,
        ParentValue = ParentText,
        IconCssClass = DrillHeaderIcon,
    };

    // Başlık ikonu: entity ikonu (yoksa generic düzenleme ikonu).
    private string DrillHeaderIcon => string.IsNullOrEmpty(EntityIcon) ? FrameworkIcons.Edit : EntityIcon!;

    // Toolbar (liste ToolbarRenderer + edit EditToolbar) aksiyon SONRASI tek tazeleme noktası: Click EventCallback'i
    // o bileşenlerde üretildiğinden Blazor DrillList'i otomatik render etmez → durum (popup aç/kapa, seçim, _busy)
    // yansısın diye burada StateHasChanged. Böylece aksiyon metotlarına serpilmiş StateHasChanged'lere gerek kalmaz.
    private void OnToolbarActionInvoked() => StateHasChanged();

    private void StartNew()
    {
        StartNewItem(NewItemFactory());
    }

    /// <summary>HAZIR bir öğe ile yeni-kayıt popup'ını açar — split "Yeni" alt aksiyonları için (çağıran öğeyi
    /// kurar; NewItemFactory devre dışı). MaxItems sınırı yine uygulanır. Custom action'dan çağrıldığında
    /// render'ı kendisi tetikler (Click EventCallback'i DrillList dışında üretilmiş olabilir).</summary>
    public void StartNewItem(TItem item)
    {
        // Üst sınır emniyeti — buton pasifken de (Kaydet&Yeni yolu) taşma olmasın.
        if (MaxItemsReached)
        {
            UiService.ShowWarningToast(L["DrillMaxItems"].Value);
            return;
        }

        _editItem = item;
        _editOriginal = null;
        _isNew = true;
        SetEditContext(_editItem);
        _popupVisible = true;
        _pendingEditRender = true;
        StateHasChanged();
    }

    public void EditItem(TItem item)
    {
        _editOriginal = item;
        _editItem = CloneFactory != null ? CloneFactory(item) : item;  // kopya → Cancel geri alabilir
        _isNew = false;
        SetEditContext(_editItem);
        _popupVisible = true;
        _pendingEditRender = true;
    }

    // Tek tıkla satırdan düzenleme formu açılır (ayrı "Düzelt" butonu yok).
    private void OnRowClick(GridRowClickEventArgs e)
    {
        if (ReadOnly) return;
        if (e.Grid.GetDataItem(e.VisibleIndex) is not TItem item) return;
        EditItem(item);
    }

    // Satır tıkla = inline düzenle → düzenlenebilir drill'de satır üzerinde el (pointer) cursor (CSS sınıfı yok).
    private void OnCustomizeRow(GridCustomizeElementEventArgs e)
    {
        if (e.ElementType == GridElementType.DataRow && !ReadOnly)
            e.Style = "cursor: pointer;";
    }

    // ── Kolon düzeni kalıcılığı — TxGrid (StateKey) devraldı; tam anahtar burada türetilip TxGrid'e geçer. ──
    /// <summary>Aynı TItem'i farklı bağlamda drill edersen ayırt etmek için opsiyonel anahtar.</summary>
    [Parameter] public string? StateKey { get; set; }
    // v2: seçim kolonu sola-sabit + VisibleIndex=0 standardına geçince eski kayıtlı düzenler geçersiz (kolon sırası değişti).
    private string GridStateKey => $"Drill:{StateKey ?? typeof(TItem).Name}:v2";

    // Footer EditForm DIŞINDA olduğundan submit'i elle tetikliyoruz: önce doğrula, geçerliyse kaydet.
    private async Task HandleSaveClick()
    {
        if (_editContext == null) return;
        if (_editContext.Validate())
            await SaveAsync();
        else
            ShowValidationToasts();   // normal edit formlarıyla parite: validation hataları DxToast olarak
    }

    // Kaydet ve Yeni: geçerliyse kaydet, başarılıysa (SaveAsync popup'ı kapattıysa) hemen yeni kayıt aç.
    private async Task HandleSaveAndNew()
    {
        if (_editContext == null) return;
        if (!_editContext.Validate()) { ShowValidationToasts(); return; }
        await SaveAsync();
        if (!_popupVisible)  // başarı → SaveAsync kapattı → yenisini aç (hata olduysa açık kalır, dokunma)
            StartNew();
    }

    // Validation hatalarını normal edit formlarıyla AYNI şekilde göster: her mesaj AYRI DxToast (XAF tarzı,
    // CrudEditComponentBase.ShowError ile aynı desen). Popup'taki inline ValidationSummary'ye EK — bu app'te
    // Bootstrap alert stili etkisiz olduğundan toast asıl görünür bildirimdir.
    // Mantık merkezî EditContextValidationExtensions'ta (ValueObjectEditPopup ile AYNI yol — kopya YOK).
    // Bağlam öneki: hangi kaydın alanı olduğu mesajda görünür ("Şirket FMS → Şube HQ: Kod alanı zorunludur.").
    private void ShowValidationToasts()
    {
        _editContext.ShowValidationToasts(UiService, BuildValidationPath());
    }

    /// <summary>Üstten cascade gelen bağlam yolu — iç içe drill'de dış popup'ın kimliği ("Şirket FMS").</summary>
    [CascadingParameter(Name = "ValidationPathPrefix")] private string? InheritedValidationPath { get; set; }

    /// <summary>Bu popup'ın bağlam yolu: üst zincir + kendi kimliği (EntityName + kaydın Code/başlığı).
    /// Toast öneki VE alt bileşenlere cascade değeri olarak aynı metin kullanılır (tek kaynak).</summary>
    private string? BuildValidationPath()
    {
        var identity = _editItem != null ? PrimaryTextSelector?.Invoke(_editItem) : null;
        var own = string.IsNullOrEmpty(identity)
            ? EntityName
            : string.IsNullOrEmpty(EntityName) ? identity : $"{EntityName} {identity}";
        return EditContextValidationExtensions.CombinePath(InheritedValidationPath, own);
    }

    private async Task SaveAsync()
    {
        if (_editItem == null) return;

        // Kaydetme engeli (SaveGuard): mesaj varsa uyar + popup açık kal (validation'la aynı UX).
        if (SaveGuard?.Invoke(_editItem) is { } saveGuardMessage)
        {
            UiService.ShowWarningToast(saveGuardMessage);
            return;
        }

        _busy = true;
        try
        {
            if (_isNew)
            {
                var added = Persistent ? await PersistCreate!(_editItem) : _editItem;
                Items.Add(added);
                _selectedItems = new List<object> { added };
            }
            else
            {
                // Düzenleme: clone üzerinde çalışıldıysa orijinal slotu değiştir.
                var target = _editOriginal ?? _editItem;
                if (Persistent) _editItem = await PersistUpdate!(_editItem);
                var idx = Items.IndexOf(target);
                if (idx >= 0) Items[idx] = _editItem;
                _selectedItems = new List<object> { _editItem };
            }

            var saved = SelectedItem;
            _popupVisible = false;
            _editItem = null;
            _editOriginal = null;
            ClearEditContext();
            if (saved != null) await OnItemSaved.InvokeAsync(saved);
            await OnSelectionChanged.InvokeAsync(saved);
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            ShowExceptionToast(ex);   // lokalize (BusinessException kodu → mesaj); ham ex.Message toast'ı KALDIRILDI.
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cancel()
    {
        _popupVisible = false;
        _editItem = null;
        _editOriginal = null;
        ClearEditContext();
    }

    // ── ISplitEditActions — drill edit popup'ı genel EDIT TOOLBAR'ı (EditToolbar) AYNEN kullanır (ikiz yok).
    //    Basit in-memory item edit: yalnız Kaydet + Kaydet&Yeni görünür; Sil/nav/undo/reset yeteneği YOK (gizli). ──
    bool ISplitEditActions.CanSave => (_isNew || IsEditDirty) && !_busy;
    bool ISplitEditActions.IsNew => _isNew;
    bool ISplitEditActions.IsReadOnly => false;
    string? ISplitEditActions.ReadOnlyNotice => null;
    Task ISplitEditActions.SaveAsync() => HandleSaveClick();
    Task ISplitEditActions.SaveAndNewAsync() => HandleSaveAndNew();
    Task ISplitEditActions.SaveAndCloseAsync() => HandleSaveClick();   // drill Save zaten popup'ı kapatır

    // Elle ekleme kapalıysa (AllowAdd=false) "Kaydet ve Yeni" gizli; silme kapalıysa Sil gizli.
    bool ISplitEditActions.SupportsSaveAndNew => AllowAdd;
    bool ISplitEditActions.SupportsDelete => AllowDelete;

    // Sil — düzenlenen öğeyi sil (grid Sil ile AYNI guard/MinItems/persist üzerinden), sonra popup'ı kapat.
    bool ISplitEditActions.CanDelete => AllowDelete && !_isNew && !_busy;
    async Task ISplitEditActions.DeleteAsync()
    {
        if (_editItem == null || _isNew) return;
        _selectedItems = new List<object> { (object)(_editOriginal ?? _editItem) };
        await DeleteSelected();                       // MinItems + DeleteGuard + soft/persist + OnChanged
        if (_selectedItems == null) Cancel();         // silindi (seçim temizlendi) → popup'ı kapat
    }

    // Reset — kaydedilmemiş değişiklikleri at: snapshot'tan düzenlenen öğeyi geri yükle (ana edit ile aynı UX).
    Task ISplitEditActions.ResetAsync()
    {
        if (_editSnapshot != null && _editItem != null)
        {
            try
            {
                var restored = System.Text.Json.JsonSerializer.Deserialize<TItem>(_editSnapshot);
                if (restored != null) { _editItem = restored; SetEditContext(_editItem); }
            }
            catch { /* fail-safe: bırak */ }
        }
        return Task.CompletedTask;   // re-render toolbar OnActionInvoked'tan gelir (EditToolbar → OnToolbarActionInvoked)
    }

    bool ISplitEditActions.CanGoPrevious => false;
    bool ISplitEditActions.CanGoNext => false;
    Task ISplitEditActions.GoPreviousAsync() => Task.CompletedTask;
    Task ISplitEditActions.GoNextAsync() => Task.CompletedTask;
    bool ISplitEditActions.SupportsRecordNavigation => false;
    Task<bool> ISplitEditActions.CanLeaveAsync() => Task.FromResult(true);   // kapanış guard'ı OnPopupClosing'de
    bool ISplitEditActions.CanUndo => false;
    bool ISplitEditActions.CanRedo => false;
    Task ISplitEditActions.UndoAsync() => Task.CompletedTask;
    Task ISplitEditActions.RedoAsync() => Task.CompletedTask;
    bool ISplitEditActions.SupportsUndoRedo => false;
    void ISplitEditActions.NotifyInput() { }
    void ISplitEditActions.CommitUndoStep() { }

    // List toolbar'ın custom aksiyonları (public CustomActions parametresi) EDIT toolbar'ına SIZMASIN. EditToolbar
    // E.CustomActions'ı da basar; public CustomActions ISplitEditActions.CustomActions'ı (aynı ad → implicit) karşıladığı
    // için list aksiyonları edit popup'ında da görünürdü. Explicit null → list aksiyonları YALNIZ list toolbar'da.
    IReadOnlyList<CrudToolbarAction>? ISplitEditActions.CustomActions => null;

    // Kapanma ÖNCESİ (Closing): kullanıcı kapanışında (X/Escape) kirli ise onay; reddedilirse iptal → açık kal.
    private async Task OnPopupClosing(PopupClosingEventArgs args)
    {
        if (args.CloseReason == PopupCloseReason.Programmatically) return;  // Save/Cancel kodu zaten kapattı
        if (!await ConfirmCloseAsync()) args.Cancel = true;
    }

    // Kapanış kesinleşti (EditShell.OnClosedConfirmed; parametresiz) → temizle. Pencere durumunu EditShell sıfırlar.
    private void OnPopupClosed()
    {
        _editItem = default;
        _editOriginal = default;
        if (_popupVisible) Cancel();
    }

    // Dirty değilse serbest. Dirty ise "Kaydet / Yoksay / (çarpı=İptal)" — ana edit formuyla aynı UX:
    // Kaydet→doğrula+kaydet (geçerliyse kapanır, geçersizse açık kalır), Yoksay→değişiklikleri at, İptal→kal.
    private async Task<bool> ConfirmCloseAsync()
    {
        if (!IsEditDirty) return true;
        var result = await UiService.ConfirmAsync(
            L["UnsavedChangesConfirmation"].Value, title: null,
            yesText: L["SaveChanges"].Value, noText: L["DiscardChanges"].Value,
            showCancel: false, defaultYes: true);
        return result switch
        {
            ConfirmDialogResult.Yes => await TrySaveForCloseAsync(),  // geçerli+kaydedildi → kapat; geçersiz → kal
            ConfirmDialogResult.No  => true,                          // yoksay → kapat
            _                       => false,                         // çarpı/iptal → kal
        };
    }

    // Kapanışta Kaydet: geçersizse false (açık kal); geçerliyse kaydet. Kapanma kararı SaveAsync'in SONUCUNA
    // bakar (_popupVisible): SaveGuard/persist hatası kaydı bloklarsa popup açık kalır — "Kaydet" dediği halde
    // değişikliğin sessizce atılması olmaz (HandleSaveAndNew ile aynı desen).
    private async Task<bool> TrySaveForCloseAsync()
    {
        if (_editContext == null) return false;
        if (!_editContext.Validate()) { ShowValidationToasts(); return false; }
        await SaveAsync();
        return !_popupVisible;
    }

    private async Task DeleteSelected()
    {
        var toDelete = _selectedItems?.OfType<TItem>().ToList();
        if (toDelete == null || toDelete.Count == 0) return;

        if (FilteredItems.Count() - toDelete.Count < MinItems)   // görünür (silinmemiş) sayıya göre
        {
            UiService.ShowWarningToast(L["DrillMinItems"].Value);
            return;
        }

        foreach (var item in toDelete)
        {
            var guard = DeleteGuard?.Invoke(item);
            if (guard != null)
            {
                UiService.ShowWarningToast(guard);
                return;
            }
        }

        _busy = true;
        try
        {
            foreach (var item in toDelete)
            {
                if (SoftDelete)
                {
                    // Yeni (Idsiz) → listeden tamamen çıkar; mevcut → IsDeleted işaretle (arka planda kalır).
                    if (IsNewItem?.Invoke(item) == true)
                        Items.Remove(item);
                    else
                        MarkDeleted?.Invoke(item);
                    await OnItemDeleted.InvokeAsync(item);
                }
                else
                {
                    if (Persistent && PersistDelete != null)
                        await PersistDelete(item);
                    await OnItemDeleted.InvokeAsync(item);
                    Items.Remove(item);
                }
            }
            _selectedItems = null;
            await OnSelectionChanged.InvokeAsync(null);
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            ShowExceptionToast(ex);   // lokalize (BusinessException kodu → mesaj); ham ex.Message toast'ı KALDIRILDI.
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- AllowInlineEdit (DevExpress native EditRow — hücre editörü modu) ----

    // Yeni satır: edit model'i kendi NewItemFactory'mizle üret (ClientKey + default'lar gelsin).
    private void OnCustomizeEditModel(GridCustomizeEditModelEventArgs e)
    {
        if (e.IsNew) e.EditModel = NewItemFactory();
    }

    // Kaydet: popup SaveAsync ile aynı mantık (persistent → sunucu kopyası; in-memory → canlı nesne).
    private async Task OnEditModelSaving(GridEditModelSavingEventArgs e)
    {
        if (e.EditModel is not TItem model) return;

        // Kaydetme engeli — popup yoluyla AYNI sözleşme (DeleteGuard paritesi): uyar + satır editte kalsın.
        if (SaveGuard?.Invoke(model) is { } saveGuardMessage)
        {
            UiService.ShowWarningToast(saveGuardMessage);
            e.Cancel = true;
            return;
        }

        _busy = true;
        try
        {
            if (e.IsNew)
            {
                var added = Persistent ? await PersistCreate!(model) : model;
                Items.Add(added);
                await OnItemSaved.InvokeAsync(added);
            }
            else if (Persistent)
            {
                // Sunucu taze kopyayı döner → liste slotunu onunla değiştir.
                var updated = await PersistUpdate!(model);
                if (e.DataItem is TItem target)
                {
                    var idx = Items.IndexOf(target);
                    if (idx >= 0) Items[idx] = updated;
                }
                await OnItemSaved.InvokeAsync(updated);
            }
            else
            {
                // In-memory: kullanıcı değişikliklerini canlı veri öğesine kopyala.
                e.CopyChangesToDataItem();
                if (e.DataItem is TItem saved) await OnItemSaved.InvokeAsync(saved);
            }
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            ShowExceptionToast(ex);   // lokalize (BusinessException kodu → mesaj); ham ex.Message toast'ı KALDIRILDI.
        }
        finally
        {
            _busy = false;
        }
    }

    // Sil: popup DeleteSelected ile aynı kurallar (MinItems + DeleteGuard + persist).
    private async Task OnDataItemDeleting(GridDataItemDeletingEventArgs e)
    {
        if (e.DataItem is not TItem item) return;

        if (Items.Count - 1 < MinItems)
        {
            UiService.ShowWarningToast(L["DrillMinItems"].Value);
            return;
        }
        var guard = DeleteGuard?.Invoke(item);
        if (guard != null)
        {
            UiService.ShowWarningToast(guard);
            return;
        }

        _busy = true;
        try
        {
            if (Persistent && PersistDelete != null) await PersistDelete(item);
            await OnItemDeleted.InvokeAsync(item);
            Items.Remove(item);
            await OnChanged.InvokeAsync();
        }
        catch (Exception ex)
        {
            ShowExceptionToast(ex);   // lokalize (BusinessException kodu → mesaj); ham ex.Message toast'ı KALDIRILDI.
        }
        finally
        {
            _busy = false;
        }
    }
}
