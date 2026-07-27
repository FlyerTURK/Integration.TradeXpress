using DevExpress.Blazor;
using Integration.Framework.Blazor.Client.Services.Mdi;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    public partial class CrudLayout<TGetDto, TListDto, TKey> : IDisposable, ISplitGridActions
    {
        [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? GridAttributes { get; set; }

        [Parameter] public bool ValidateOnPropertyChange { get; set; } = true;

        [Parameter, EditorRequired] public ICrudStateService<TListDto, TKey> StateService { get; set; } = default!;

        /// <summary>SplitCrudView birleşik toolbar host'u; doluysa yerel CrudToolbar çizilmez.</summary>
        [CascadingParameter] public ISplitHost? SplitHost { get; set; }

        [Inject] protected IUiStateService? UiStateService { get; set; }

        [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

        /// <summary>Liste görünüm durumu (arama metni + aktif/pasif filtresi) MDI sekmesiyle kalıcılaşsın mı.
        /// Varsayılan true ("tüm sayfalarda iç durum" hedefi); sayfa bazında kapatılabilir. Grid KOLON düzeni
        /// bundan bağımsız — o zaten IUiStateService kanalında per-grid kalıcı.</summary>
        [Parameter] public bool PersistTabViewState { get; set; } = true;

        /// <summary>İçinde yaşadığımız MDI sekmesi (uygulama MDI sağlamıyorsa null → kalıcılık no-op).</summary>
        [CascadingParameter(Name = "CurrentMdiTab")] private IMdiTab? CurrentMdiTab { get; set; }

        // App TabManager'ı IMdiTabOpener olarak kaydeder; MDI'sız host'ta null (CrudPageBase ile aynı desen).
        private IMdiTabOpener? TabOpener => (IMdiTabOpener?)ServiceProvider.GetService(typeof(IMdiTabOpener));

        /// <summary>Sekmeyle kalıcılaşan liste görünüm durumu (TabPageState sözleşmesi).</summary>
        private sealed record CrudListTabState(string? SearchText, bool? ActiveFilter);

        /// <summary>Server-side grid veri kaynağı (CrudPageBase.GridDataSource). Verilirse DxGrid server-mode'a geçer.</summary>
        [Parameter] public object? DataSource { get; set; }

        /// <summary>Kolon filtre satırını (FilterRow) gösterir. Varsayılan açık. Server tarafı kolon filtresini
        /// (ApplyListRequest → request.Filters) İŞLEYEN sayfalarda açık bırakılır. ApplyListRequest kullanmayan,
        /// sabit/in-memory sıralayan ve input.Filters'ı yok sayan servislerin sayfaları (ör. CurrencyUnitMargin)
        /// bunu <c>false</c> geçmeli — aksi halde kullanıcı filtreler ama sonuç değişmez (yanıltıcı).</summary>
        [Parameter] public bool ShowColumnFilter { get; set; } = true;

        /// <summary>Arama kutusu metni değişince sayfaya bildirir (server-side filtre).</summary>
        [Parameter] public EventCallback<string> OnSearchChanged { get; set; }

        /// <summary>Arama modu. Varsayılan ServerSide (toolbar araması tüm kayıtlarda → OnSearchChanged/server reload).
        /// InGrid → arama grid'in YÜKLÜ verisinde istemci filtresi (DxGrid.SearchText). İstenirse sayfa değiştirir.</summary>
        [Parameter] public GridSearchMode SearchMode { get; set; } = GridSearchMode.ServerSide;

        // DxGrid.SearchText'e bağlı (in-grid istemci filtresi). InGrid modda toolbar araması buraya yazar; ayrıca
        // mobil gömülü arama kutusu (ShowSearchBox) da bunu kullanır. ServerSide modda toolbar araması buraya değil
        // OnSearchChanged'e gider (tüm kayıtlar).
        private string? _inGridSearchText;
        [Parameter] public EventCallback OnNewClick { get; set; }

        /// <summary>Stok "Yeni" butonunu gizler (CrudToolbar'a geçilir). Polymorphic liste kendi "Yeni ▾" dropdown'ını
        /// CustomActions ile koyar. Varsayılan açık → mevcut sayfalar etkilenmez.</summary>
        [Parameter] public bool ShowNew { get; set; } = true;

        [Parameter] public EventCallback<TListDto> OnUpdateClick { get; set; }
        [Parameter] public EventCallback OnDeleteClick { get; set; }
        [Parameter] public EventCallback OnRefreshClick { get; set; }
        
        [Parameter] public EventCallback OnSaveClick { get; set; }
        [Parameter] public EventCallback OnSaveAndNewClick { get; set; }
        
        [Parameter] public RenderFragment? GridColumns { get; set; }
        [Parameter] public IEnumerable<GridColumnDefinition>? Columns { get; set; }

        [Parameter] public string? PageTitle { get; set; }
        [Parameter] public string? EntityName { get; set; }

        /// <summary>Bu entity'nin ikonu (FontAwesome class) — edit başlığında gösterilir.</summary>
        [Parameter] public string? EntityIcon { get; set; }

        /// <summary>Edit başlığında gösterilecek birincil değer seçici (genelde Code).</summary>
        [Parameter] public Func<TGetDto, string?>? PrimaryTextSelector { get; set; }

        /// <summary>Varsa üst (parent) entity adı — başlığa " - [ParentEntityName: ...]" eklenir.</summary>
        [Parameter] public string? ParentEntityName { get; set; }

        /// <summary>Üst entity gösterim metni seçici (genelde parent Code).</summary>
        [Parameter] public Func<TGetDto, string?>? ParentTextSelector { get; set; }

        /// <summary>Toolbar'a sayfaya özel ek aksiyonlar (descriptor liste, SortIndex'li — ör. "Marj Ayarla", "Şubeler").</summary>
        [Parameter] public IReadOnlyList<CrudToolbarAction>? CustomActions { get; set; }

        /// <summary>OPSİYONEL salt-okuma satır tıklama — set edildiğinde <c>IsGrantedUpdate</c>'ten BAĞIMSIZ tetiklenir ve
        /// edit yolunun ÖNÜNE geçer (read-only detay panelleri için: satıra tıkla → detay popup). Unset (default) =
        /// mevcut davranış korunur (yalnız update yetkisi varken edit açılır).</summary>
        [Parameter] public EventCallback<TListDto> OnRowSelect { get; set; }

        /// <summary>OPSİYONEL master-detail satır şablonu — set edildiğinde DxGrid her satıra genişletme düğmesi ekler ve
        /// açılınca bu şablonu satır-altında render eder (context = <see cref="GridDetailRowTemplateContext"/>,
        /// <c>context.DataItem</c> = satır DTO'su). Unset (default) = master-detail kapalı (mevcut davranış).</summary>
        [Parameter] public RenderFragment<GridDetailRowTemplateContext>? DetailRowTemplate { get; set; }

        // "Yeni" tıklaması — sayfanın popup/tab akışını çağırır.
        private async Task HandleNewClick()
        {
            await OnNewClick.InvokeAsync();
        }

        // Satır düzenleme — popup veya tab (OnUpdateClick).
        private async Task HandleRowEdit(TListDto item)
        {
            await OnUpdateClick.InvokeAsync(item);
        }

        IGrid Grid { get; set; } = default!;
        string? SearchText { get; set; }

        // Mobil arama ikonuyla açılıp kapanan DxGrid gömülü arama kutusu.
        private bool _showGridSearch;
        private void ToggleGridSearch() => _showGridSearch = !_showGridSearch;

        // Context-menu'den yönetilen filtre satırı toggle'ı (başlangıçta ShowColumnFilter'dan). Gruplama server-side
        // custom data source'ta desteklenmediği için grup paneli/komutları yok.
        private bool _showFilterRow;

        // Yerel toolbar referansı — row context menüsü onun görünür aksiyonlarından doldurulur (SplitHost null iken).
        private CrudToolbar<TGetDto, TListDto, TKey>? _toolbar;

        // Context-menu inşası (OnCustomizeContextMenu + ApplyIcon) CrudLayout.ContextMenu.cs partial dosyasında.

        // Veri satırı tıklanınca düzenleme açılır → düzenleme yetkisi varken satır üzerinde el (pointer) cursor'ı.
        // CSS sınıfı yok; DevExpress CustomizeElement API'siyle satıra inline stil verilir.
        private void OnCustomizeGridElement(GridCustomizeElementEventArgs e)
        {
            if (e.ElementType == GridElementType.DataRow && StateService.IsGrantedUpdate)
                e.Style = "cursor: pointer;";
        }

        // IsActive filtre switch durumu. İkili: true = Aktif kayıtlar, false = Pasif kayıtlar.
        private bool? _activeFilter;

        // TListDto IIsActive ise IsActive filtresi geçerlidir (yoksa whitelist'te IsActive olmaz → hata).
        private static readonly bool IsActiveFilterable =
            typeof(Integration.Framework.Base.Dtos.Interfaces.IIsActive).IsAssignableFrom(typeof(TListDto));

        // Fetched aboneliği için referans (Dispose'da çözmek için tutulur).
        private GridListDataSource<TListDto>? _gridSource;

        protected override void OnInitialized()
        {
            _showFilterRow = ShowColumnFilter;   // filtre satırı başlangıç durumu (context-menu ile toggle edilir)
            StateService.OnReloadRequested += ReloadGrid;
            // Köprü: grid'i StateService'e doğrudan bağla (SplitHost'tan bağımsız, her zaman). Popup/liste
            // sayfa-aşırı gezinme bu kayıttan grid'i sürer (GoNext/PreviousGlobalAsync → EnsurePage/FocusDataItem).
            StateService.RegisterGrid(this);
            SplitHost?.RegisterGrid(this);   // birleşik toolbar arama/filtre/export'u buradan alır
        }

        // DataSource [Parameter] OnInitialized'da henüz null olabilir (parent grid kaynağını sonra bağlar) →
        // Fetched aboneliği ve IsActive ilk-filtresi DataSource dolunca (bir kez) kurulur. Aksi halde grid
        // fetch'i state'e hiç yansımıyor (TotalCount=0 → tüm Prev/Next pasif).
        protected override void OnParametersSet()
        {
            if (_gridSource == null && DataSource is GridListDataSource<TListDto> ds)
            {
                _gridSource = ds;
                ds.Fetched += SyncStateFromGrid;            // her fetch'te global state + yüklü sayfa StateService'e

                // IIsActive grid'lerde ilk yükleme "Aktif" kayıtlar (switch varsayılan ON). İlk fetch'ten önce.
                if (IsActiveFilterable)
                {
                    _activeFilter = true;
                    ds.ActiveFilter = true;
                }

                // Sekme restore'u: kaydedilmiş liste görünümü (arama/filtre) İLK fetch'ten önce uygulanır —
                // varsayılanla bir kez çekip sonra tekrar çekmek yerine doğrudan saklı görünümle açılır.
                if (PersistTabViewState && TabPageState.TryRead<CrudListTabState>(CurrentMdiTab) is { } restored)
                {
                    if (IsActiveFilterable && restored.ActiveFilter is { } activeFilter)
                    {
                        _activeFilter = activeFilter;
                        ds.ActiveFilter = activeFilter;
                    }
                    if (!string.IsNullOrEmpty(restored.SearchText))
                    {
                        // Arama modu ne olursa olsun mevcut toolbar yolu izlenir (InGrid → istemci filtresi,
                        // ServerSide → sayfanın OnSearchChanged'i). Render sonrası kuyruklanır.
                        var text = restored.SearchText;
                        _ = InvokeAsync(() => OnToolbarSearch(text));
                    }
                }

                SyncStateFromGrid();   // abone geç kaldıysa olmuş ilk fetch'i de yakala
            }
        }

        // Grid fetch → StateService senkronu (SyncStateFromGrid) CrudLayout.GridStateSync.cs partial dosyasında.

        // ── ISplitGridActions (SplitCrudView birleşik toolbar) — mevcut yerel mantığa delege ──
        Task ISplitGridActions.SearchAsync(string text)          => OnToolbarSearch(text);
        bool ISplitGridActions.ActiveFilterSupported             => IsActiveFilterable;
        bool? ISplitGridActions.ActiveFilter                     => _activeFilter;
        Task ISplitGridActions.SetActiveFilterAsync(bool? value) => OnActiveFilterChanged(value);
        Task ISplitGridActions.ExportExcelAsync()                => ExportToExcel();
        Task ISplitGridActions.ExportPdfAsync()                  => PrintGrid();
        IReadOnlyList<CrudToolbarAction>? ISplitGridActions.CustomActions => CustomActions;
        // Split modda CrudToolbar grid'in DIŞINDA → otomatik re-render olmaz; explicit tetikle
        // ki DxGrid.ShowSearchBox güncellensin (gömülü arama kutusu görünür/gizlenir).
        Task ISplitGridActions.ToggleGridSearchAsync()           { ToggleGridSearch(); return InvokeAsync(StateHasChanged); }

        // DxGrid'in aktif sayfası (programatik sayfa-aşırı geçiş için two-way bind).
        private int _gridPageIndex;
        private int _gridPageSize = 20;   // grid'in GERÇEK sayfa boyutu (@bind-PageSize ile selector'a senkron)

        // Sayfa-aşırı: global index'in sayfasını grid'e yükle (PageIndex değiştir → fetch) + satır yüklenmesini
        // bekle → o satırın Id'sini döndür. SplitCrudView komşu kayıt yüklü sayfa dışındaysa çağırır.
        // ÜST SINIR clamp: globalIndex >= TotalCount ise out-of-range fetch (skip>=total → 0 kayıt, cache zehri)
        // doğmasın diye çağrılmaz. pageSize grid'in gerçek boyutundan (_gridPageSize) okunur → desync olmaz.
        async Task<object?> ISplitGridActions.EnsurePageForGlobalIndexAsync(int globalIndex)
        {
            if (Grid == null || globalIndex < 0 || globalIndex >= ((ISplitGridActions)this).TotalCount) return null;
            var pageSize   = _gridPageSize > 0 ? _gridPageSize : 20;
            var targetPage = globalIndex / pageSize;
            var rowInPage  = globalIndex % pageSize;

            if (_gridPageIndex != targetPage)
            {
                _gridPageIndex = targetPage;
                await InvokeAsync(StateHasChanged);   // grid PageIndex değişir → yeni sayfa fetch'i tetiklenir
            }
            await Grid.WaitForRemoteSourceRowLoadAsync(rowInPage);   // o satırın server'dan yüklenmesini bekle
            return Grid.GetDataItem(rowInPage) is TListDto item ? (object?)item.Id : null;
        }

        // Previous/Next ya da popup sayfa-aşırı gezinme → o kaydı SEÇ (odak/FocusedRow YOK; seçim tek kaynak).
        // Seçim grid'e @bind-SelectedDataItems ile yansır → satır vurgulanır + detail/toolbar senkron olur.
        async Task ISplitGridActions.FocusDataItemAsync(object? id)
        {
            if (id == null) return;
            if (DataSource is GridListDataSource<TListDto> ds)
            {
                foreach (var item in ds.CurrentItems)
                {
                    if (Equals(item.Id, id))
                    {
                        StateService.SetDataRowSelected(item);
                        await InvokeAsync(StateHasChanged);
                        return;
                    }
                }
            }
        }

        // Grid'in o an yüklü (görünür sayfa) satır anahtarları (Previous/Next gezinme).
        // GridListDataSource son fetch'in kayıtlarını tutar → Id listesi.
        System.Collections.Generic.IReadOnlyList<object> ISplitGridActions.GridVisibleKeys
        {
            get
            {
                if (DataSource is GridListDataSource<TListDto> ds)
                {
                    var list = new List<object>(ds.CurrentItems.Count);
                    foreach (var item in ds.CurrentItems)
                        if (item.Id != null) list.Add(item.Id);
                    return list;
                }
                return System.Array.Empty<object>();
            }
        }

        // ── Sayfa-aşırı sınır bilgisi ANLIK (GridVisibleKeys gibi; SyncState/Fetched timing'ine bağlı değil) ──
        long ISplitGridActions.TotalCount
            => DataSource is GridListDataSource<TListDto> d ? d.TotalCount : 0;

        int ISplitGridActions.PageSkip
            => DataSource is GridListDataSource<TListDto> d ? (d.LastRequest?.SkipCount ?? 0) : 0;

        // Param değişimi (arama/filtre/IsActive, harici save/delete) → grid'i SUNUCUDAN yeniden çeker.
        // KRİTİK: ilk açılıştaki TEMİZ liste gibi SAYFA 0'a dön. Cross-page gezinme _gridPageIndex'i ayrı
        // yoldan (EnsurePageForGlobalIndexAsync) değiştirir; param değişince burada sıfırlanır → yeni sonuç
        // kümesi eski sayfadan değil baştan gösterilir, snapshot (count+items) bayatlamaz. PageIndex'i önce
        // grid'e flush edip sonra Reload ediyoruz ki Reload page 0'ı (yeni params'la) çeksin.
        private void ReloadGrid() => InvokeAsync(async () =>
        {
            _gridPageIndex = 0;       // param değişimi → ilk sayfa
            StateHasChanged();        // PageIndex=0 parametresini grid'e geçir (render kuyruğa girer)
            await Task.Yield();       // render'ın PageIndex=0'ı grid'e UYGULAMASINA izin ver (Reload'dan ÖNCE)
            Grid?.Reload();           // grid artık page 0'da → count+items'ı yeni params ile tazele
        });

        // Switch değişince aktif server-side veri kaynağına filtreyi uygula ve grid'i yeniden çek.
        private Task OnActiveFilterChanged(bool? value)
        {
            _activeFilter = value;
            if (DataSource is GridListDataSource<TListDto> source)
            {
                source.ActiveFilter = value;
                StateService.RequestReload();   // grid'i sunucudan yeniden çeker (search ile aynı mekanizma)
            }
            PushTabViewState();
            return Task.CompletedTask;
        }

        private async Task OnToolbarSearch(string text)
        {
            SearchText = text;   // toolbar kutusu metnini sakla (gösterim senkronu)
            PushTabViewState();
            if (SearchMode == GridSearchMode.InGrid)
            {
                _inGridSearchText = text;   // grid'in yüklü verisinde istemci filtresi (server reload yok)
                StateHasChanged();
                return;
            }
            await OnSearchChanged.InvokeAsync(text);   // ServerSide: tüm kayıtlarda ara
        }

        /// <summary>Güncel liste görünümünü (arama + aktif filtre) sekmeye iter — MDI'sız host'ta no-op.</summary>
        private void PushTabViewState()
        {
            if (!PersistTabViewState || CurrentMdiTab is null || TabOpener is not { } opener) return;
            TabPageState.Write(opener, CurrentMdiTab, new CrudListTabState(SearchText, _activeFilter));
        }

        public void Dispose()
        {
            SplitHost?.UnregisterGrid(this);
            if (_gridSource != null)
                _gridSource.Fetched -= SyncStateFromGrid;
            if (StateService != null)
            {
                StateService.OnReloadRequested -= ReloadGrid;
                StateService.UnregisterGrid(this);
            }
        }

        // -- Row Events --
        private async Task OnRowClick(GridRowClickEventArgs e)
        {
            var item = (TListDto)Grid.GetDataItem(e.VisibleIndex);
            if (item == null)
            {
                return;
            }

            // Salt-okuma satır tıklama (detay panelleri): update yetkisinden BAĞIMSIZ; edit yolunun ÖNÜNE geçer.
            if (OnRowSelect.HasDelegate)
            {
                StateService.SetDataRowSelected(item);
                await OnRowSelect.InvokeAsync(item);
                return;
            }

            if (!StateService.IsGrantedUpdate)
            {
                return;
            }

            StateService.SetDataRowSelected(item);   // tıklanan satırı SEÇ (odak/FocusedRow yok) → toolbar senkron
            await HandleRowEdit(item);               // popup/edit açan (BeforeUpdate)
        }

        // -- Layout Persistence --
        // Versiyon eki: kolon yapısı değişince (linkli kolonlar/yeniden sıralama → kolon kimliği değişir)
        // eski kaydedilmiş düzen "bilinmeyen" kolonu en sağa atıyordu. Versiyon artınca eski düzen yok sayılır
        // → markup sırası geçerli olur (yeni düzen v3 altında kaydedilir).
        private string GetGridStateKey() => (PageTitle ?? typeof(TListDto).Name) + ":v3";

        private async Task OnGridLayoutAutoLoading(GridPersistentLayoutEventArgs e)
        {
            if (UiStateService == null) return;
            var json = await UiStateService.GetGridStateAsync(GetGridStateKey());
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { e.Layout = System.Text.Json.JsonSerializer.Deserialize<GridPersistentLayout>(json); }
                catch { /* parse error - pass */ }
            }
        }

        private async Task OnGridLayoutAutoSaving(GridPersistentLayoutEventArgs e)
        {
            if (UiStateService == null || e.Layout == null) return;
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(e.Layout);
                await UiStateService.SaveGridStateAsync(GetGridStateKey(), json);
            }
            catch
            {
                // Grid state storage çok küçük; truncate hatası sessizce yoksay.
                // Grid layout/columns kaybolsa bile grid çalışmaya devam etsin.
            }
        }

        // -- Export Logic --
        // Export assembly lazy-load'ı paylaşılan IGridExportAssemblyLoader'a taşındı (DrillList de aynısını kullanır).
        // Server'da no-op, WASM'da ilk export'ta lazy-load (açılış payload'ı küçük kalsın diye boot'tan çıkarıldılar).
        [Inject] private IGridExportAssemblyLoader ExportLoader { get; set; } = default!;

        private async Task ExportToExcel()
        {
            await ExportLoader.EnsureLoadedAsync();
            await Grid.ExportToXlsxSafeAsync("Export");
        }

        private async Task PrintGrid()
        {
            await ExportLoader.EnsureLoadedAsync();
            await Grid.ExportToPdfSafeAsync("Export");
        }
    }
}

