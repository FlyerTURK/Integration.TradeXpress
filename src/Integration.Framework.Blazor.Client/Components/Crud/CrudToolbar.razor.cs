using System.Collections.Generic;
using System.Linq;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Integration.Framework.Blazor.Client.Services.Base;

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

        // ── Üç bağlam (View mode) ──
        private bool IsSplit => SplitHost != null;
        private bool IsEdit  => SplitHost == null && EditController != null;
        private bool IsList  => SplitHost == null && EditController == null;

        // Edit aksiyonları: split'te host'un edit'i, standalone edit'te controller. Ortak sözleşme.
        private ISplitEditActions? ActiveEdit => SplitHost?.Edit ?? EditController;

        // ── Görünürlük (matris) ──
        private bool CanCreate => IsSplit ? (SplitHost!.List?.CanCreate ?? false) : (StateService?.IsGrantedCreate ?? false);
        private bool ShowNewItem    => !IsEdit && CanCreate;                 // Liste + Split
        // Kaydet: Split + Edit'te HER ZAMAN görünür; seçim/dirty yoksa Enabled=false ile pasif.
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
        private bool ShowNav        => !IsList && !(ActiveEdit?.IsNew ?? false);
        private bool ShowUndoRedo   => !IsList;                              // Split + Edit, her zaman (pasif olabilir)

        // ── Etkinlik ──
        private bool DeleteEnabled => IsEdit
            ? (ActiveEdit?.CanDelete ?? false)
            : (IsSplit ? (SplitHost!.List?.HasSelection ?? false) : (StateService?.SelectedDataItems is { Count: > 0 }));
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

        private List<CrudToolbarAction> BuildActions()
        {
            var list = new List<CrudToolbarAction>
        {
            // Yeni (Liste + Split)
            new() { SortIndex = 0, Text = L["New"], Tooltip = L["New"],
                    IconUrl = "/images/xaf/action_new.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowNewItem, OnClick = DoNew },

            // Kaydet (Split + Edit, primary) — Yeni ile Sil arasında
            new() { SortIndex = 10, Text = L["Save"], Tooltip = L["Save"], Primary = true,
                    IconUrl = "/images/xaf/action_save.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowSaveGroup, Enabled = EditCanSave, OnClick = DoSave },

            // Kaydet ve Yeni ▾ (içinde Kaydet ve Kapat) (Edit)
            new() { SortIndex = 20, Text = L["SaveAndNew"], Tooltip = L["SaveAndNew"], SplitDropDownButton = true,
                    IconUrl = "/images/xaf/action_save_new.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowSaveAndNew, Enabled = EditCanSave, OnClick = DoSaveNew,
                    Items = new List<CrudToolbarAction>
                    {
                        new() { Text = L["SaveAndClose"], Tooltip = L["SaveAndClose"],
                                IconCssClass = "fas fa-circle-check xaf-toolbar-item-icon",
                                Enabled = EditCanSave, OnClick = DoSaveClose },
                    } },

            // Sil (her yerde)
            new() { SortIndex = 100, Text = L["Delete"], Tooltip = L["Delete"],
                    IconUrl = "/images/xaf/action_delete.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowDeleteItem, Enabled = DeleteEnabled, OnClick = DoDelete },

            // (Sayfaya özel custom action'lar SortIndex'leriyle aşağıda AddRange ile eklenir — varsayılan 300,
            //  yani Sil(100) ile Arama(400) arası.)

            // Arama kutusu (masaüstü) / ikonu (mobil)
            new() { SortIndex = 400, Visible = ShowSearchBox, Template = SearchBoxTemplate },
            new() { SortIndex = 400, Tooltip = L["Search"],
                    IconUrl = "/images/xaf/action_search.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowSearchIcon, OnClick = DoToggleSearch },

            // Dışa aktar
            new() { SortIndex = 500, AdaptiveText = L["Export"], Tooltip = L["Export"],
                    IconUrl = "/images/xaf/action_export.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowExport,
                    Items = new List<CrudToolbarAction>
                    {
                        new() { Text = L["ExportToExcel"], Tooltip = L["ExportToExcel"],
                                IconUrl = "/images/xaf/action_export_toxlsx.svg", IconCssClass = "xaf-toolbar-item-icon", OnClick = DoExportExcel },
                        new() { Text = L["PrintPdf"], Tooltip = L["PrintPdf"],
                                IconUrl = "/images/xaf/action_export_topdf.svg", IconCssClass = "xaf-toolbar-item-icon", OnClick = DoExportPdf },
                    } },

            // Yenile
            new() { SortIndex = 600, AdaptiveText = L["Refresh"], Tooltip = L["Refresh"],
                    IconUrl = "/images/xaf/action_refresh.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowRefresh, OnClick = DoRefresh },

            // Previous / Next (Split + Edit) — XAF SVG (disabled görünümü Sil gibi doğru çalışır)
            new() { SortIndex = 700, AdaptiveText = L["Previous"], Tooltip = L["Previous"],
                    IconUrl = "/images/xaf/action_navigation_previous_object.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowNav, Enabled = CanPrev, OnClick = DoPrev },
            new() { SortIndex = 710, AdaptiveText = L["Next"], Tooltip = L["Next"],
                    IconUrl = "/images/xaf/action_navigation_next_object.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowNav, Enabled = CanNext, OnClick = DoNext },

            // Undo / Redo / Reset (Split + Edit)
            new() { SortIndex = 800, AdaptiveText = L["Undo"], Tooltip = L["Undo"],
                    IconCssClass = "fas fa-rotate-left xaf-toolbar-item-icon",
                    Visible = ShowUndoRedo, Enabled = CanUndo, OnClick = DoUndo },
            new() { SortIndex = 810, AdaptiveText = L["Redo"], Tooltip = L["Redo"],
                    IconCssClass = "fas fa-rotate-right xaf-toolbar-item-icon",
                    Visible = ShowUndoRedo, Enabled = CanRedo, OnClick = DoRedo },
            new() { SortIndex = 820, AdaptiveText = L["Reset"], Tooltip = L["Reset"],
                    IconCssClass = "fas fa-eraser xaf-toolbar-item-icon",
                    Visible = ShowUndoRedo, Enabled = CanReset, OnClick = DoReset },

            // IsActive switch — EN SONDA, sağa yaslı (Liste + Split)
            new() { SortIndex = 1000, Alignment = ToolbarItemAlignment.Right,
                    Visible = ShowFilter, Template = ActiveFilterTemplate },
        };

            // Sayfa-özel custom action'lar — yalnız Liste + Split (edit'te gizli). SortIndex'leri sayfa belirler.
            if (!IsEdit)
                list.AddRange(EffectiveCustomActions);

            return list;
        }
    }
}
