using DevExpress.Blazor;

namespace Integration.Framework.Blazor.Client.Components.Crud;

// Kolon başlığı + satır context-menu inşası — okunabilirlik için ayrı partial dosyada.
public partial class CrudLayout<TGetDto, TListDto, TKey>
{
    // Kolon başlığı + satır context menüsü: built-in (kolon seçici, grup paneli, filtre builder, sort, gizle) +
    // ek kolaylaştırıcılar. Başlık: filtre satırı göster/gizle + filtreyi temizle. Satır: toolbar kopyası.
    private void OnCustomizeContextMenu(GridCustomizeContextMenuEventArgs args)
    {
        GridColumnFixMenu.Add(args);   // Sola/Sağa Sabitle + Kaldır (seçim kolonu hariç) — TxGrid ile ortak, her grid'de aynı
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
            search.IconCssClass = "custom-icon-search";
        }
    }

    // Context menü öğesine ikon uygula. IContextMenuItem'da IconUrl doluysa IconCssClass yok sayılır;
    // bu yüzden önce URL (XAF SVG), yoksa CSS class (FontAwesome custom action) denenir.
    private static void ApplyIcon(IContextMenuItem item, string? iconUrl, string? iconCssClass)
    {
        if (!string.IsNullOrEmpty(iconUrl)) item.IconUrl = iconUrl;
        else if (!string.IsNullOrEmpty(iconCssClass)) item.IconCssClass = iconCssClass;
    }
}
