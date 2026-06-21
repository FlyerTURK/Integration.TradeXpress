using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Integration.Framework.Blazor.Client.Services.Base;

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

        /// <summary>Server-side grid veri kaynağı (CrudPageBase.GridDataSource). Verilirse DxGrid server-mode'a geçer.</summary>
        [Parameter] public object? DataSource { get; set; }

        /// <summary>Kolon filtre satırını (FilterRow) gösterir. Varsayılan açık. Server tarafı kolon filtresini
        /// (ApplyListRequest → request.Filters) İŞLEYEN sayfalarda açık bırakılır. ApplyListRequest kullanmayan,
        /// sabit/in-memory sıralayan ve input.Filters'ı yok sayan servislerin sayfaları (ör. CurrencyUnitMargin)
        /// bunu <c>false</c> geçmeli — aksi halde kullanıcı filtreler ama sonuç değişmez (yanıltıcı).</summary>
        [Parameter] public bool ShowColumnFilter { get; set; } = true;

        /// <summary>Arama kutusu metni değişince sayfaya bildirir (server-side filtre).</summary>
        [Parameter] public EventCallback<string> OnSearchChanged { get; set; }
        [Parameter] public EventCallback OnNewClick { get; set; }
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

        // Kolon başlığı + satır context menüsü: built-in (kolon seçici, grup paneli, filtre builder, sort, gizle) +
        // ek kolaylaştırıcılar. Başlık: filtre satırı göster/gizle + filtreyi temizle. Satır: toolbar kopyası.
        private void OnCustomizeContextMenu(GridCustomizeContextMenuEventArgs args)
        {
            if (args.Context is GridHeaderCommandContext)
            {
                // Server-side custom data source GRUPLAMAYI desteklemiyor (GetGroupInfoAsync implement değil) →
                // grup komutlarını kaldır; aksi halde "Group By Column" çöker.
                args.Items.Remove(GridContextMenuDefaultItemNames.GroupByColumn);
                args.Items.Remove(GridContextMenuDefaultItemNames.UngroupColumn);
                args.Items.Remove(GridContextMenuDefaultItemNames.ShowGroupPanel);

                var filterRow = args.Items.AddCustomItem(L["FilterRow"].Value, () =>
                {
                    _showFilterRow = !_showFilterRow;
                    return InvokeAsync(StateHasChanged);
                });
                filterRow.BeginGroup = true;
                args.Items.AddCustomItem(L["ClearFilter"].Value, () =>
                {
                    Grid?.SetFilterCriteria(null);
                    return Task.CompletedTask;
                });
            }
            else if (args.Context is GridDataRowCommandContext rowContext)
            {
                // Önce sağ-tıklanan satırı SEÇ → toolbar aksiyonları (Sil/Marj Ayarla vb.) bu satıra göre Enabled hesaplar.
                if (rowContext.DataItem is TListDto item)
                    StateService.SetDataRowSelected(item);

                // Row context menü = TOOLBAR'ın o anki görünür item'ları (dinamik; hardcoded değil). Çift satır
                // düzenleme: satır tıkla = düzenle zaten var; burada toolbar ne sunuyorsa o doldurulur.
                // Yerel toolbar (standalone) yoksa split host'un birleşik toolbar aksiyonlarını kullan.
                var menuActions = _toolbar?.MenuActions ?? SplitHost?.ToolbarMenuActions;
                if (menuActions != null)
                {
                    var first = true;
                    foreach (var a in menuActions)
                    {
                        var text = a.Text ?? a.Tooltip ?? a.AdaptiveText;
                        if (string.IsNullOrEmpty(text)) continue;
                        var onClick = a.OnClick;
                        var ci = args.Items.AddCustomItem(text, () => onClick?.Invoke() ?? Task.CompletedTask);
                        ci.Enabled = a.Enabled;
                        ApplyIcon(ci, a.IconUrl, a.IconCssClass);
                        if (first) { ci.BeginGroup = true; first = false; }

                        // Alt menü öğeleri (ör. Kaydet&Yeni → Kaydet&Kapat) — hepsini göster.
                        if (a.Items != null)
                            foreach (var sub in a.Items)
                            {
                                var subText = sub.Text ?? sub.Tooltip ?? sub.AdaptiveText;
                                if (string.IsNullOrEmpty(subText)) continue;
                                var subClick = sub.OnClick;
                                var sci = ci.Items.AddCustomItem(subText, () => subClick?.Invoke() ?? Task.CompletedTask);
                                sci.Enabled = sub.Enabled;
                                ApplyIcon(sci, sub.IconUrl, sub.IconCssClass);
                            }
                    }
                }

                // Arama: toolbar'da kutu (aksiyon değil) → menüye ayrı ekle (grid içi arama kutusunu aç/kapat).
                var search = args.Items.AddCustomItem(L["Search"].Value, () =>
                {
                    ToggleGridSearch();
                    return InvokeAsync(StateHasChanged);
                });
                search.BeginGroup = true;
                search.IconCssClass = "fas fa-magnifying-glass";
            }
        }

        // Context menü öğesine ikon uygula. IContextMenuItem'da IconUrl doluysa IconCssClass yok sayılır;
        // bu yüzden önce URL (XAF SVG), yoksa CSS class (FontAwesome custom action) denenir.
        private static void ApplyIcon(IContextMenuItem item, string? iconUrl, string? iconCssClass)
        {
            if (!string.IsNullOrEmpty(iconUrl)) item.IconUrl = iconUrl;
            else if (!string.IsNullOrEmpty(iconCssClass)) item.IconCssClass = iconCssClass;
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
                SyncStateFromGrid();   // abone geç kaldıysa olmuş ilk fetch'i de yakala
            }
        }

        // Grid her fetch ettiğinde (Fetched) merkezi StateService'i tazele: yüklü sayfa + sayfa-aşırı durumu.
        // (Grid fetch'i CrudLayout'u re-render etmediğinden eski OnAfterRender senkronu güvenilmezdi.)
        private void SyncStateFromGrid()
        {
            if (_gridSource is not { } ds) return;
            var req = ds.LastRequest;
            InvokeAsync(async () =>
            {
                StateService.ListDataSource = new List<TListDto>(ds.CurrentItems);
                StateService.TotalCount     = ds.TotalCount;
                if (req != null)
                {
                    StateService.PageSkip       = req.SkipCount;
                    StateService.PageSize       = req.MaxResultCount;
                    StateService.Sorts          = req.Sorts;
                    StateService.Filter         = req.Filter;
                    StateService.IsActiveFilter = req.IsActive;
                }

                // Düz liste sayfası: fetch sonrası mevcut seçili kayıt yeni yüklü sayfada hâlâ varsa korunur
                // (örn. popup sayfa-aşırı o kaydı seçtiyse); yoksa İLK kayıt görsel odaklanır (FocusDataItemAsync →
                // FocusedRowChanged → OnGridFocusedRowChanged → SelectedDataItems+SelectedItem), sayfa boşsa seçim
                // temizlenir. Böylece sayfa değişince eski sayfanın kaydı Sil'e açık kalmaz. Split kendi grid
                // focus'unu yönettiği için (SplitHost!=null) buraya girmez.
                if (SplitHost == null)
                {
                    var current = StateService.SelectedItem;
                    var stillThere = false;
                    if (current != null)
                        foreach (var it in ds.CurrentItems)
                            if (Equals(it.Id, current.Id)) { stillThere = true; break; }
                    if (!stillThere)
                    {
                        if (ds.CurrentItems.Count > 0)
                            await ((ISplitGridActions)this).FocusDataItemAsync(ds.CurrentItems[0].Id);
                        else
                            StateService.SetDataRowSelected(null!);
                    }
                }

                StateHasChanged();
            });
        }

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

        // Previous/Next ya da seçili kayıt → grid'de o satırı odakla + görünür yap (gerekirse scroll/sayfa).
        async Task ISplitGridActions.FocusDataItemAsync(object? id)
        {
            if (id == null || Grid == null) return;
            if (DataSource is GridListDataSource<TListDto> ds)
            {
                foreach (var item in ds.CurrentItems)
                {
                    if (Equals(item.Id, id))
                    {
                        // SetFocusedDataItemAsync → FocusedRowChanged → selection senkronu (tek kaynak).
                        await Grid.SetFocusedDataItemAsync(item);   // odak + scroll/sayfa + seçim
                        return;
                    }
                }
            }
        }

        // Grid odağı (focus) değişince o satırı SEÇİLİ yap → focus+selection görsel tutarlılığı (split + düz liste).
        // İlk yüklemede / sayfa değişiminde DevExpress ilk satırı otomatik focus eder; bu kanca onu selection'a
        // (SelectedDataItems → SelectedItem) yansıtır → tek kaynak. Previous/Next (FocusDataItemAsync) ve tıklama
        // da buradan senkron. Popup sayfa-aşırı gezinmede grid'i hedefe taşıdıktan sonra FocusDataItemAsync(target)
        // ile odak hedefe gider → bu kanca seçimi de hedefe yazar (son söz). Tüm modlarda çalışır.
        private void OnGridFocusedRowChanged(GridFocusedRowChangedEventArgs e)
        {
            // Idempotent: odak zaten seçili kayıttaysa no-op → geç gelen FocusedRowChanged'in (sayfa/satır
            // değişiminde) seçimi başka kayda ezmesini önler (çok-yazarlı focus/selection yarışı kesilir).
            if (StateService.SelectedItem != null && e.DataItem is TListDto fi && Equals(fi.Id, StateService.SelectedItem.Id))
                return;
            StateService.SelectedDataItems = e.DataItem is TListDto item
                ? new List<TListDto> { item }
                : new List<TListDto>();
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
            return Task.CompletedTask;
        }

        private async Task OnToolbarSearch(string text)
        {
            SearchText = text;
            await OnSearchChanged.InvokeAsync(text);
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
            if (!StateService.IsGrantedUpdate)
            {
                return;
            }
            var item = (TListDto)Grid.GetDataItem(e.VisibleIndex);
            if (item != null)
            {
                await Grid.SetFocusedDataItemAsync(item);   // tıklanan satırı odakla → OnGridFocusedRowChanged seçimi senkronlar
                await HandleRowEdit(item);                  // popup/edit açan (BeforeUpdate)
            }
        }

        // -- Layout Persistence --
        private string GetGridStateKey() => PageTitle ?? typeof(TListDto).Name;

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
        // Export'a özel ağır DevExpress assembly'leri (Pdf/Printing/Drawing) boot'tan çıkarıldı
        // (csproj BlazorWebAssemblyLazyLoad). İlk export tıklamasında burada yüklenir; sonraki
        // tıklamalarda runtime cache'inden gelir (idempotent). Açılış payload'ı ~10MB daha küçük.
        [Inject] private LazyAssemblyLoader? LazyAssemblyLoader { get; set; }

        private static readonly string[] ExportAssemblies =
        {
            "DevExpress.Printing.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Core.wasm",
            "DevExpress.Pdf.v25.2.Drawing.wasm",
            "DevExpress.Drawing.v25.2.wasm",
        };

        private bool _exportAssembliesLoaded;

        private async Task EnsureExportAssembliesAsync()
        {
            if (_exportAssembliesLoaded) return;
            if (!OperatingSystem.IsBrowser() || LazyAssemblyLoader == null)
            {
                _exportAssembliesLoaded = true;
                return;
            }
            await LazyAssemblyLoader.LoadAssembliesAsync(ExportAssemblies);
            _exportAssembliesLoaded = true;
        }

        private async Task ExportToExcel()
        {
            await EnsureExportAssembliesAsync();
            await Grid.ExportToXlsxSafeAsync("Export");
        }

        private async Task PrintGrid()
        {
            await EnsureExportAssembliesAsync();
            await Grid.ExportToPdfSafeAsync("Export");
        }
    }
}
