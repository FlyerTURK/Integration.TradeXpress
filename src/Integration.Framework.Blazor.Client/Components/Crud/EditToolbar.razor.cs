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

        /// <summary>Aksiyon çalıştıktan sonra host'a (EntityEditForm/DrillList) iletilir → durum sahibi kendini
        /// render eder (ToolbarRenderer'ın receiver'ı bu bileşen olduğundan host otomatik tazelenmez).</summary>
        [Parameter] public EventCallback OnActionInvoked { get; set; }

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

        // Kimlik merkezî CrudToolbarActions kataloğundan (CrudToolbar/DrillList ile AYNI); burada yalnız
        // ISplitEditActions yetenek bayraklarından gelen Visible/Enabled/OnClick → EntityEditForm değişmez.
        private List<CrudToolbarAction> BuildActions()
        {
            var actions = new List<CrudToolbarAction>
            {
                CrudToolbarActions.Save(L, visible: true, E.CanSave, DoSave),
                CrudToolbarActions.SaveAndNew(L, visible: E.SupportsSaveAndNew, E.CanSave, splitDropDown: !IsPopupEdit, DoSaveNew, IsPopupEdit ? null : DoSaveClose),
                CrudToolbarActions.Delete(L, visible: E.SupportsDelete, E.CanDelete, DoDelete),
                CrudToolbarActions.Previous(L, ShowNav, E.CanGoPrevious, DoPrev),
                CrudToolbarActions.Next(L, ShowNav, E.CanGoNext, DoNext),
                CrudToolbarActions.Undo(L, ShowUndoRedo, E.CanUndo, DoUndo),
                CrudToolbarActions.Redo(L, ShowUndoRedo, E.CanRedo, DoRedo),
                CrudToolbarActions.Reset(L, visible: true, E.CanSave, DoReset),
            };

            // Edite özel ek aksiyonlar (ör. Order "Kabul Et"/"Reddet") — SortIndex'leri host belirler, Delete(100)
            // ile Previous(700) arasına yerleşir (liste toolbar'ının custom=300 slotuyla AYNI aralık).
            if (E.CustomActions is { Count: > 0 } custom)
            {
                actions.AddRange(custom);
            }

            return actions;
        }
    }
}
