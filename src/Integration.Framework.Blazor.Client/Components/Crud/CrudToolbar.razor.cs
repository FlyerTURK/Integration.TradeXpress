using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    public partial class CrudToolbar<TGetDto, TListDto, TKey>
    {
        // Liste modunda CrudLayout verir (callback'ler + StateService).
        [Parameter] public ICrudStateService<TListDto, TKey>? StateService { get; set; }

        [Parameter] public string? SearchText { get; set; }
        [Parameter] public EventCallback<string> SearchTextChanged { get; set; }

        [Parameter] public EventCallback OnNewClick { get; set; }
        [Parameter] public EventCallback OnDeleteClick { get; set; }
        [Parameter] public EventCallback OnRefreshClick { get; set; }

        /// <summary>Stok "Yeni" butonunu gizler (varsayılan açık). Polymorphic liste gibi sayfalar kendi tipe-özel
        /// "Yeni ▾" dropdown'ını CustomActions ile koyar → çift buton olmasın diye stok Yeni'yi kapatır.</summary>
        [Parameter] public bool ShowNew { get; set; } = true;

        [Parameter] public EventCallback OnExportToExcelClick { get; set; }
        [Parameter] public EventCallback OnPrintPdfClick { get; set; }

        /// <summary>Sayfaya özel ek toolbar aksiyonları (ör. "Marj Ayarla") — descriptor liste, SortIndex'li.</summary>
        [Parameter] public IReadOnlyList<CrudToolbarAction>? CustomActions { get; set; }

        [Parameter] public bool? ActiveFilter { get; set; }
        [Parameter] public EventCallback<bool?> ActiveFilterChanged { get; set; }

        /// <summary>SplitCrudView cascade'i — doluysa "Split" modu (liste+edit yan yana).</summary>
        [CascadingParameter] public ISplitHost? SplitHost { get; set; }

        /// <summary>CrudEditShell verir — doluysa "Edit" modu (standalone popup/sekme edit).</summary>
        [Parameter] public ISplitEditActions? EditController { get; set; }

        /// <summary>GlobalPopupHost cascade'i — doluysa edit POPUP'ta açık (tab/split/standalone değil).</summary>
        [CascadingParameter] public IPopupChrome? PopupChrome { get; set; }

        // ── Üç bağlam (View mode) ──
        private bool IsSplit => SplitHost != null;
        private bool IsEdit  => SplitHost == null && EditController != null;
        private bool IsList  => SplitHost == null && EditController == null;
        // Popup'ta açık edit: Save zaten Save&Close yapıyor → ayrı "Kaydet ve Kapat" gereksiz.
        private bool IsPopupEdit => PopupChrome != null;

        // Edit aksiyonları: split'te host'un edit'i, standalone edit'te controller. Ortak sözleşme.
        private ISplitEditActions? ActiveEdit => SplitHost?.Edit ?? EditController;

        // ── Görünürlük (matris) ──
        private bool CanCreate => IsSplit ? (SplitHost!.List?.CanCreate ?? false) : (StateService?.IsGrantedCreate ?? false);
        private bool ShowNewItem    => !IsEdit && CanCreate && ShowNew;      // Liste + Split (ShowNew=false → gizli)
        // Kaydet: Split + Edit'te HER ZAMAN görünür; seçim/dirty yoksa Enabled=false ile pasif.
        // Salt-okunur edit'te (ör. tenant'ta global birim) butonlar GÖRÜNÜR ama Enabled=false (CanSave/CanDelete).
        private bool ShowSaveGroup  => !IsList;
        // Kaydet&Yeni / Kaydet&Kapat: YALNIZ standalone Edit formunda (split'te tek Kaydet yeter).
        private bool ShowSaveAndNew => IsEdit;
        private bool ShowDeleteItem => IsEdit                                 // Her yerde
            ? (ActiveEdit != null)
            : (IsSplit ? (SplitHost!.List?.CanDelete ?? false) : (StateService?.IsGrantedDelete ?? false));
        private bool ShowSearchBox  => !IsEdit && !IsMobile;                 // Liste + Split (masaüstü)
        private bool ShowSearchIcon => !IsEdit && IsMobile;                  // Liste + Split (mobil)
        private bool ShowExport     => !IsEdit;                              // Liste + Split
        private bool ShowRefresh    => !IsEdit;                              // Liste + Split
        private bool ShowFilter     => !IsEdit && ShowActiveFilter;          // Liste + Split (IIsActive)
        // Previous/Next: Split + Edit. Split'te SplitHost (grid keys), popup/standalone edit'te
        // merkezi StateService (GoNext/GoPreviousRecord) üzerinden gezinir. YENİ kayıtta gizli.
        private bool ShowNav        => !IsList && !(ActiveEdit?.IsNew ?? false) && (ActiveEdit?.SupportsRecordNavigation ?? true);
        private bool ShowUndoRedo   => !IsList && (ActiveEdit?.SupportsUndoRedo ?? true);   // edit destekliyorsa
        private bool ShowReset      => !IsList;                              // Reset her edit'te (snapshot'tan geri al)

        // Tenant'ın multi-tenancy bağlamı — host (global) kayıt koruması için. Property injection (LazyServiceProvider)
        // Blazor.Client tiplerinde güvenilmez (DependsOn dışı) → açıkça @inject; base.CurrentTenant'ı GİZLEMEMEK için ayrı ad.
        [Inject] private Volo.Abp.MultiTenancy.ICurrentTenant TenantContext { get; set; } = default!;

        // ── Etkinlik ──
        private bool DeleteEnabled => IsEdit
            ? (ActiveEdit?.CanDelete ?? false)
            : (HasListSelection && !SelectionHasProtectedGlobal);

        private bool HasListSelection =>
            IsSplit ? (SplitHost!.List?.HasSelection ?? false) : (StateService?.SelectedDataItems is { Count: > 0 });

        // Tenant oturumunda seçimde HERHANGİ bir host (global) kayıt varsa (tekli ya da çoklu) Sil pasif:
        // host kataloğu kayıtları tenant tarafından silinemez. Host oturumda (TenantContext.Id==null) kısıt yok.
        // TListDto IHostScoped değilse (ör. tenant-only Company/Branch/Vault) kısıt uygulanmaz.
        private bool SelectionHasProtectedGlobal
        {
            get
            {
                if (TenantContext?.Id == null) return false;
                var items = StateService?.SelectedDataItems;
                if (items == null || items.Count == 0) return false;
                return items.OfType<Integration.Framework.Base.Dtos.Interfaces.IHostScoped>().Any(x => x.IsGlobal);
            }
        }
        private bool EditCanSave => ActiveEdit?.CanSave ?? false;
        private bool CanUndo  => ActiveEdit?.CanUndo ?? false;
        private bool CanRedo  => ActiveEdit?.CanRedo ?? false;
        private bool CanReset => EditCanSave;
        // Gezinme: split'te SplitHost (grid keys), popup/standalone edit'te ActiveEdit (merkezi StateService).
        private bool CanPrev  => IsSplit ? (SplitHost?.CanGoPrevious ?? false) : (ActiveEdit?.CanGoPrevious ?? false);
        private bool CanNext  => IsSplit ? (SplitHost?.CanGoNext ?? false) : (ActiveEdit?.CanGoNext ?? false);

        // Split modda host grid'inden, normalde parametreden gelen sayfa-özel custom action descriptor'ları.
        private IReadOnlyList<CrudToolbarAction> EffectiveCustomActions =>
            (IsSplit ? SplitHost!.Grid?.CustomActions : CustomActions) ?? System.Array.Empty<CrudToolbarAction>();

        // Plain (ERPPROV3 gibi) — item arka planlarını DOLDURMAZ, temanın doğal renklerini kullanır
        // (Contained, Blazing Dark'ta arka planı beyaz dolduruyordu). Kaydet RenderStyle=Primary ile vurgulu kalır.
        private DevExpress.Blazor.ToolbarRenderStyleMode RenderMode => DevExpress.Blazor.ToolbarRenderStyleMode.Plain;

        // ── Aksiyon yönlendirme (bağlama göre) ──
        private Task DoNew()     => IsSplit ? (SplitHost!.List?.NewAsync() ?? Task.CompletedTask) : OnNewClick.InvokeAsync();
        private Task DoDelete()  => IsEdit
            ? (ActiveEdit?.DeleteAsync() ?? Task.CompletedTask)               // Edit: açık kaydı sil
            : IsSplit ? (SplitHost!.List?.DeleteAsync() ?? Task.CompletedTask)  // Split: seçili satır(lar)
                      : OnDeleteClick.InvokeAsync();                          // Liste: seçili satır(lar)
        private Task DoRefresh() => IsSplit ? (SplitHost!.List?.RefreshAsync() ?? Task.CompletedTask) : OnRefreshClick.InvokeAsync();
        private Task DoExportExcel() => IsSplit ? (SplitHost!.Grid?.ExportExcelAsync() ?? Task.CompletedTask) : OnExportToExcelClick.InvokeAsync();
        private Task DoExportPdf()   => IsSplit ? (SplitHost!.Grid?.ExportPdfAsync()   ?? Task.CompletedTask) : OnPrintPdfClick.InvokeAsync();
        private Task DoToggleSearch() => IsSplit ? (SplitHost!.Grid?.ToggleGridSearchAsync() ?? Task.CompletedTask) : OnToggleGridSearch.InvokeAsync();

        private Task DoSave()     => ActiveEdit?.SaveAsync()        ?? Task.CompletedTask;
        private Task DoSaveNew()  => ActiveEdit?.SaveAndNewAsync()  ?? Task.CompletedTask;
        private Task DoSaveClose()=> ActiveEdit?.SaveAndCloseAsync()?? Task.CompletedTask;
        private Task DoUndo()  => ActiveEdit?.UndoAsync() ?? Task.CompletedTask;
        private Task DoRedo()  => ActiveEdit?.RedoAsync() ?? Task.CompletedTask;
        private Task DoReset() => ActiveEdit?.ResetAsync()?? Task.CompletedTask;
        private Task DoPrev()  => IsSplit ? (SplitHost?.GoPreviousAsync() ?? Task.CompletedTask) : (ActiveEdit?.GoPreviousAsync() ?? Task.CompletedTask);
        private Task DoNext()  => IsSplit ? (SplitHost?.GoNextAsync()     ?? Task.CompletedTask) : (ActiveEdit?.GoNextAsync()     ?? Task.CompletedTask);

        // TListDto IIsActive implement ediyorsa IsActive filtre switch'i (liste/split) gösterilir.
        private static readonly bool ShowActiveFilter =
            typeof(Integration.Framework.Base.Dtos.Interfaces.IIsActive).IsAssignableFrom(typeof(TListDto));

        private bool ActiveSwitchValue => (IsSplit ? SplitHost!.Grid?.ActiveFilter : ActiveFilter) != false;
        private Task OnActiveSwitchChanged(bool on)
            => IsSplit ? (SplitHost!.Grid?.SetActiveFilterAsync(on) ?? Task.CompletedTask)
                       : ActiveFilterChanged.InvokeAsync((bool?)on);

        [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

        [Parameter] public EventCallback OnToggleGridSearch { get; set; }

        private string? _localSearchText;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (_localSearchText != SearchText)
            {
                _localSearchText = SearchText;
            }
        }

        private void OnLocalTextChanged(string newText)
        {
            _localSearchText = newText;
            _ = DoSearch(_localSearchText);
        }

        private Task OnSearchButtonClick() => DoSearch(_localSearchText ?? string.Empty);

        private Task DoSearch(string text)
            => IsSplit ? (SplitHost!.Grid?.SearchAsync(text) ?? Task.CompletedTask)
                       : SearchTextChanged.InvokeAsync(text);

        // ── ERPPROV3 kalıbı: tüm aksiyonlar TEK listede, SortIndex'e göre sıralanıp tek foreach ile
        //    render edilir → DxToolbar'ın render/register timing'i devre dışı, pozisyon yalnız SortIndex'ten.
        //    Görünmeyenler elenir ama sıra korunur (custom action async gelse de yeri sabit kalır). ──
        private IEnumerable<CrudToolbarAction> SortedActions =>
            BuildActions()
                .Where(a => a.Visible)
                .OrderBy(a => a.Alignment == ToolbarItemAlignment.Right ? 1 : 0)
                .ThenBy(a => a.SortIndex);

        /// <summary>Toolbar'ın o anki görünür aksiyonları (CrudLayout satır context menüsünü bundan doldurur).
        /// BuildActions her erişimde StateService'i okur → çağrı anındaki seçim/dirty durumuna göre Enabled doğru.</summary>
        public IReadOnlyList<CrudToolbarAction> MenuActions => SortedActions.ToList();

        private List<CrudToolbarAction> BuildActions()
        {
            // Kimlik (SortIndex/ikon/Text/grup/alt-item) merkezî CrudToolbarActions kataloğundan; burada yalnız
            // bağlama göre değişen wiring (Visible/Enabled/OnClick/Template). EditToolbar + DrillList AYNI katalog.
            var list = new List<CrudToolbarAction>
            {
                CrudToolbarActions.New(L, ShowNewItem, true, DoNew),
                CrudToolbarActions.Save(L, ShowSaveGroup, EditCanSave, DoSave),
                CrudToolbarActions.SaveAndNew(L, ShowSaveAndNew, EditCanSave, splitDropDown: !IsPopupEdit, DoSaveNew, IsPopupEdit ? null : DoSaveClose),
                CrudToolbarActions.Delete(L, ShowDeleteItem, DeleteEnabled, DoDelete),
                CrudToolbarActions.Export(L, ShowExport, DoExportExcel, DoExportPdf),
                CrudToolbarActions.Refresh(L, ShowRefresh, true, DoRefresh),
                CrudToolbarActions.Previous(L, ShowNav, CanPrev, DoPrev),
                CrudToolbarActions.Next(L, ShowNav, CanNext, DoNext),
                CrudToolbarActions.Undo(L, ShowUndoRedo, CanUndo, DoUndo),
                CrudToolbarActions.Redo(L, ShowUndoRedo, CanRedo, DoRedo),
                CrudToolbarActions.Reset(L, ShowReset, CanReset, DoReset),
                CrudToolbarActions.ActiveFilter(ShowFilter, ActiveFilterTemplate),
            };

            // ARAMA: kutu/ikon kararı merkezî fabrikada (DrillList de AYNI fabrikayı kullanır) — eşik tek yerde.
            list.AddRange(CrudToolbarActions.Search(L, !IsEdit, IsMobile, SearchBoxTemplate, DoToggleSearch));

            // Sayfa-özel custom action'lar — yalnız Liste + Split (edit'te gizli). SortIndex'leri sayfa belirler.
            if (!IsEdit)
                list.AddRange(EffectiveCustomActions);

            return list;
        }
    }
}
