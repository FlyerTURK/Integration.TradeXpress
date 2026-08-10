using DevExpress.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Components.Crud
{
    /// <summary>
    /// Stok toolbar aksiyonlarının KİMLİK tek kaynağı (ERPPROV3 SortIndex kalıbı). CrudToolbar (list/split/edit),
    /// EditToolbar ve DrillList liste toolbar'ı bu fabrikayı çağırır → SortIndex / ikon / Text-anahtarı / hizalama /
    /// grup / alt-item şekli TEK yerde tanımlı. Değişken kısımlar (Visible / Enabled / OnClick / Template) çağırandan
    /// gelir; her host kendi görünürlük/yetki/wiring mantığını korur. Lokalizasyon için <paramref name="L"/> geçilir
    /// (üç host da CrudComponentBase → IStringLocalizer L var; statik global state yok, test edilebilir).
    ///
    /// <para>Stok SortIndex slotları: New=0, Save=10, SaveAndNew=20, Delete=100, [custom=300], Search=400, Export=500,
    /// Refresh=600, Previous=700, Next=710, Undo=800, Redo=810, Reset=820, ActiveFilter(Right)=1000.</para>
    /// </summary>
    internal static class CrudToolbarActions
    {
        private const string IconClass = "xaf-toolbar-item-icon";

        public static CrudToolbarAction New(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 0, Text = L["New"], Tooltip = L["New"],
            IconUrl = "/images/xaf/action_new.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Save(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 10, Text = L["Save"], Tooltip = L["Save"], Primary = true,
            IconUrl = "/images/xaf/action_save.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        /// <summary>Kaydet ve Yeni (▾). <paramref name="onSaveAndClose"/> null ise düz buton (popup'ta Save zaten kapatır);
        /// doluysa SplitDropDown + "Kaydet ve Kapat" alt item'ı. Alt item Enabled = ana <paramref name="enabled"/>.</summary>
        public static CrudToolbarAction SaveAndNew(IStringLocalizer L, bool visible, bool enabled, bool splitDropDown,
                                                   Func<Task> onClick, Func<Task>? onSaveAndClose) => new()
        {
            SortIndex = 20, Text = L["SaveAndNew"], Tooltip = L["SaveAndNew"], SplitDropDownButton = splitDropDown,
            IconUrl = "/images/xaf/action_save_new.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
            Items = onSaveAndClose == null ? null : new List<CrudToolbarAction>
            {
                new() { Text = L["SaveAndClose"], Tooltip = L["SaveAndClose"],
                        IconCssClass = "custom-icon-check-circle " + IconClass,
                        Enabled = enabled, OnClick = onSaveAndClose },
            },
        };

        public static CrudToolbarAction Delete(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 100, Text = L["Delete"], Tooltip = L["Delete"],
            IconUrl = "/images/xaf/action_delete.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        /// <summary>Masaüstü arama kutusu (Template host'tan; DxTextBox kendi event'ini taşır).</summary>
        public static CrudToolbarAction SearchBox(bool visible, RenderFragment<IToolbarItemInfo> template) => new()
        {
            SortIndex = 400, Visible = visible, Template = template,
        };

        /// <summary>Mobil arama ikonu (grid gömülü arama kutusunu aç/kapat).</summary>
        public static CrudToolbarAction SearchIcon(IStringLocalizer L, bool visible, Func<Task> onClick) => new()
        {
            SortIndex = 400, Tooltip = L["Search"],
            IconUrl = "/images/xaf/action_search.svg", IconCssClass = IconClass,
            Visible = visible, OnClick = onClick,
        };

        /// <summary>
        /// ARAMA — kutu mu ikon mu, kararı TEK YERDE (2026-08-10 Hakan kuralı: "dar ekranda arama kutusu
        /// sadece ikonlu butona dönüşsün; bunu merkezi yap").
        ///
        /// <para><b>Neden fabrika:</b> kural iki toolbar'da da geçerli (liste/split <c>CrudToolbar</c> ve
        /// <c>DrillList</c>). Her toolbar kendi <c>ShowSearchBox</c>/<c>ShowSearchIcon</c> ikilisini yazarsa
        /// eşik bir yerde değişip diğerinde kalır ve aynı uygulamada iki farklı davranış olur. Burada tek
        /// karar var: <paramref name="isNarrow"/> ise ikon, değilse kutu — ikisi ASLA birlikte görünmez.</para>
        ///
        /// <para>Dar ekranda kutuyu ezerek göstermek (daralan "A..." kutusu) en kötü seçenekti: hem
        /// yazılamıyor hem yer kaplıyordu.</para>
        /// </summary>
        public static IEnumerable<CrudToolbarAction> Search(
            IStringLocalizer L,
            bool visible,
            bool isNarrow,
            RenderFragment<IToolbarItemInfo> boxTemplate,
            Func<Task> onToggle)
        {
            yield return SearchBox(visible && !isNarrow, boxTemplate);
            yield return SearchIcon(L, visible && isNarrow, onToggle);
        }

        public static CrudToolbarAction Export(IStringLocalizer L, bool visible, Func<Task> onExcel, Func<Task> onPdf) => new()
        {
            SortIndex = 500, AdaptiveText = L["Export"], Tooltip = L["Export"],
            IconUrl = "/images/xaf/action_export.svg", IconCssClass = IconClass,
            Visible = visible,
            Items = new List<CrudToolbarAction>
            {
                new() { Text = L["ExportToExcel"], Tooltip = L["ExportToExcel"],
                        IconUrl = "/images/xaf/action_export_toxlsx.svg", IconCssClass = IconClass, OnClick = onExcel },
                new() { Text = L["PrintPdf"], Tooltip = L["PrintPdf"],
                        IconUrl = "/images/xaf/action_export_topdf.svg", IconCssClass = IconClass, OnClick = onPdf },
            },
        };

        public static CrudToolbarAction Refresh(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 600, AdaptiveText = L["Refresh"], Tooltip = L["Refresh"],
            IconUrl = "/images/xaf/action_refresh.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Previous(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 700, AdaptiveText = L["Previous"], Tooltip = L["Previous"],
            IconUrl = "/images/xaf/action_navigation_previous_object.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Next(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 710, AdaptiveText = L["Next"], Tooltip = L["Next"],
            IconUrl = "/images/xaf/action_navigation_next_object.svg", IconCssClass = IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Undo(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 800, AdaptiveText = L["Undo"], Tooltip = L["Undo"],
            IconCssClass = "custom-icon-refresh " + IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Redo(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 810, AdaptiveText = L["Redo"], Tooltip = L["Redo"],
            IconCssClass = "custom-icon-refresh " + IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        public static CrudToolbarAction Reset(IStringLocalizer L, bool visible, bool enabled, Func<Task> onClick) => new()
        {
            SortIndex = 820, AdaptiveText = L["Reset"], Tooltip = L["Reset"],
            IconCssClass = "custom-icon-eraser " + IconClass,
            Visible = visible, Enabled = enabled, OnClick = onClick,
        };

        /// <summary>IsActive Aktif/Pasif switch'i — sağa yaslı, en sonda (Template host'tan; DxCheckBox kendi event'ini taşır).</summary>
        public static CrudToolbarAction ActiveFilter(bool visible, RenderFragment<IToolbarItemInfo> template) => new()
        {
            SortIndex = 1000, Alignment = ToolbarItemAlignment.Right, Visible = visible, Template = template,
        };
    }
}

