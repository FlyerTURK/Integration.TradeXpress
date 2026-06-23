using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// Edit aksiyon toolbar'ı — TEK kaynak. EntityEditForm (standalone/popup/tab) ve DrillList edit popup'ı
    /// AYNI bu bileşeni kullanır (ikiz değil). Yalnız <see cref="ISplitEditActions"/> sözleşmesine bağlı
    /// (generic değil) → her edit bağlamı kullanabilir. Görünür aksiyonlar edit'in yetenek bayraklarından gelir.
    /// </summary>
    public partial class EditToolbar
    {
        /// <summary>Edit aksiyonlarının kaynağı (EntityEditForm veya DrillList kendini verir).</summary>
        [Parameter, EditorRequired] public ISplitEditActions EditController { get; set; } = default!;

        /// <summary>GlobalPopupHost cascade'i — doluysa edit POPUP'ta açık → Save zaten Save&Close yapar,
        /// "Kaydet ve Yeni" ▾ alt-item'ı (Kaydet&Kapat) gereksiz → düz buton.</summary>
        [CascadingParameter] public IPopupChrome? PopupChrome { get; set; }

        [CascadingParameter(Name = "IsMobile")] public bool IsMobile { get; set; }

        /// <summary>Popup bağlamını ZORLA — drill kendi DxPopup'ında açılır (GlobalPopupHost PopupChrome cascade'i YOK),
        /// ama yine popup'tır → true verince Save = Save&Close kabul edilir, "Kaydet ve Yeni ▾" alt-item'ı (Kaydet&Kapat) gizlenir.</summary>
        [Parameter] public bool IsPopup { get; set; }

        private ISplitEditActions E => EditController;
        private bool IsPopupEdit => PopupChrome != null || IsPopup;

        // ── Görünürlük (edit yetenekleri) ──
        private bool ShowNav      => !E.IsNew && E.SupportsRecordNavigation;
        private bool ShowUndoRedo => E.SupportsUndoRedo;

        private DevExpress.Blazor.ToolbarRenderStyleMode RenderMode => DevExpress.Blazor.ToolbarRenderStyleMode.Plain;

        // ── Aksiyon yönlendirme ──
        private Task DoSave()      => E.SaveAsync();
        private Task DoSaveNew()   => E.SaveAndNewAsync();
        private Task DoSaveClose() => E.SaveAndCloseAsync();
        private Task DoDelete()    => E.DeleteAsync();
        private Task DoPrev()      => E.GoPreviousAsync();
        private Task DoNext()      => E.GoNextAsync();
        private Task DoUndo()      => E.UndoAsync();
        private Task DoRedo()      => E.RedoAsync();
        private Task DoReset()     => E.ResetAsync();

        private IEnumerable<CrudToolbarAction> SortedActions =>
            BuildActions().Where(a => a.Visible).OrderBy(a => a.SortIndex);

        // CrudToolbar'ın edit alt-kümesiyle BİRE BİR aynı (SortIndex/ikon/primary) → EntityEditForm değişmez.
        private List<CrudToolbarAction> BuildActions() => new()
        {
            // Kaydet (primary, Contained)
            new() { SortIndex = 10, Text = L["Save"], Tooltip = L["Save"], Primary = true,
                    IconUrl = "/images/xaf/action_save.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = true, Enabled = E.CanSave, OnClick = DoSave },

            // Kaydet ve Yeni ▾ (Kaydet ve Kapat) — popup'ta düz buton (Save zaten kapatır).
            new() { SortIndex = 20, Text = L["SaveAndNew"], Tooltip = L["SaveAndNew"], SplitDropDownButton = !IsPopupEdit,
                    IconUrl = "/images/xaf/action_save_new.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = true, Enabled = E.CanSave, OnClick = DoSaveNew,
                    Items = IsPopupEdit ? null : new List<CrudToolbarAction>
                    {
                        new() { Text = L["SaveAndClose"], Tooltip = L["SaveAndClose"],
                                IconCssClass = "fas fa-circle-check xaf-toolbar-item-icon",
                                Enabled = E.CanSave, OnClick = DoSaveClose },
                    } },

            // Sil — her edit'te görünür; yeni/silinemez kayıtta Enabled=false (CanDelete) ile pasif.
            new() { SortIndex = 100, Text = L["Delete"], Tooltip = L["Delete"],
                    IconUrl = "/images/xaf/action_delete.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = true, Enabled = E.CanDelete, OnClick = DoDelete },

            // Önceki / Sonraki (kayıt-arası gezinme destekliyorsa)
            new() { SortIndex = 700, AdaptiveText = L["Previous"], Tooltip = L["Previous"],
                    IconUrl = "/images/xaf/action_navigation_previous_object.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowNav, Enabled = E.CanGoPrevious, OnClick = DoPrev },
            new() { SortIndex = 710, AdaptiveText = L["Next"], Tooltip = L["Next"],
                    IconUrl = "/images/xaf/action_navigation_next_object.svg", IconCssClass = "xaf-toolbar-item-icon",
                    Visible = ShowNav, Enabled = E.CanGoNext, OnClick = DoNext },

            // Geri al / Yinele (undo/redo destekliyorsa)
            new() { SortIndex = 800, AdaptiveText = L["Undo"], Tooltip = L["Undo"],
                    IconCssClass = "fas fa-rotate-left xaf-toolbar-item-icon",
                    Visible = ShowUndoRedo, Enabled = E.CanUndo, OnClick = DoUndo },
            new() { SortIndex = 810, AdaptiveText = L["Redo"], Tooltip = L["Redo"],
                    IconCssClass = "fas fa-rotate-right xaf-toolbar-item-icon",
                    Visible = ShowUndoRedo, Enabled = E.CanRedo, OnClick = DoRedo },

            // Reset — her edit'te görünür (snapshot'tan geri al); değişiklik yoksa Enabled=false (CanSave).
            new() { SortIndex = 820, AdaptiveText = L["Reset"], Tooltip = L["Reset"],
                    IconCssClass = "fas fa-eraser xaf-toolbar-item-icon",
                    Visible = true, Enabled = E.CanSave, OnClick = DoReset },
        };
    }
}
