using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// SplitCrudView, tek birleşik toolbar çizebilmek için liste ve edit panellerinin
/// aksiyonlarını bu host üzerinden toplar. Cascade ile panellere ulaşır;
/// paneller mount olunca kendilerini register eder, unmount olunca temizler.
/// Host varsa liste/edit kendi yerel toolbar'ını ÇİZMEZ (tek toolbar kuralı).
/// </summary>
public interface ISplitHost
{
    void RegisterList(ISplitListActions list);
    void RegisterEdit(ISplitEditActions edit);
    void UnregisterEdit(ISplitEditActions edit);

    /// <summary>Grid'e özgü aksiyonlar (arama/filtre/export) CrudLayout'tan register edilir.</summary>
    void RegisterGrid(ISplitGridActions grid);
    void UnregisterGrid(ISplitGridActions grid);

    /// <summary>Buton durumu (seçim, dirty) değişince toolbar'ı yeniden çizdirir.</summary>
    void NotifyChanged();

    // ── Merkezi CrudToolbar split modda buradan beslenir ──
    /// <summary>Register edilmiş liste aksiyonları (null = liste henüz mount değil).</summary>
    ISplitListActions? List { get; }
    /// <summary>Register edilmiş grid aksiyonları (arama/filtre/export/custom).</summary>
    ISplitGridActions? Grid { get; }
    /// <summary>Aktif edit paneli (null = seçim yok / edit mount değil).</summary>
    ISplitEditActions? Edit { get; }
    /// <summary>Birleşik (split) toolbar'ın o anki görünür aksiyonları — CrudLayout satır context menüsü
    /// split modda yerel toolbar olmadığından bunu kullanır.</summary>
    System.Collections.Generic.IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction> ToolbarMenuActions { get; }
    /// <summary>Bir kayıt seçili/yeni mi (edit paneli açık mı).</summary>
    bool HasSelection { get; }
    /// <summary>Mobil görünüm mü (MainLayout viewport sinyalinden).</summary>
    bool IsMobile { get; }

    // ── Seçim değişimi tek noktadan, dirty-guard ile geçer ──
    // Açık edit kirliyse önce discard onayı sorulur; reddedilirse geçiş iptal.

    /// <summary>Liste satırı seçildi → edit panelini o kayda geçir (guard'lı).</summary>
    Task RequestSelectAsync(object? id);

    /// <summary>Yeni kayıt → edit panelini boş forma geçir (guard'lı).</summary>
    Task RequestNewAsync();

    /// <summary>Geri/Kapat → seçimi sıfırla, listeye dön (guard'lı).</summary>
    Task RequestCloseAsync();

    // ── Kayıt gezinme (Previous/Next) — liste'deki komşu kayda geçer (guard'lı) ──
    bool CanGoPrevious { get; }
    bool CanGoNext { get; }
    Task GoPreviousAsync();
    Task GoNextAsync();
}

/// <summary>Liste panelinin birleşik toolbar'a sunduğu aksiyonlar.</summary>
public interface ISplitListActions
{
    bool CanCreate { get; }
    bool CanDelete { get; }
    bool HasSelection { get; }

    Task NewAsync();
    Task DeleteAsync();
    Task RefreshAsync();
}

/// <summary>
/// Grid'e özgü aksiyonlar — CrudLayout sağlar (grid + GridDataSource orada).
/// Arama, Aktif/Pasif filtresi ve Excel/PDF export birleşik toolbar'a buradan bağlanır.
/// </summary>
public interface ISplitGridActions
{
    Task SearchAsync(string text);

    /// <summary>TListDto IIsActive ise true → toolbar'da Aktif/Pasif switch'i gösterilir.</summary>
    bool ActiveFilterSupported { get; }
    bool? ActiveFilter { get; }
    Task SetActiveFilterAsync(bool? value);

    Task ExportExcelAsync();
    Task ExportPdfAsync();

    /// <summary>Sayfaya özel toolbar aksiyonları (descriptor liste, SortIndex'li) — CrudLayout'a verilen CustomActions.</summary>
    System.Collections.Generic.IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction>? CustomActions { get; }

    /// <summary>Mobil arama ikonu → grid'in gömülü arama kutusunu aç/kapat.</summary>
    Task ToggleGridSearchAsync();

    /// <summary>Grid'in o an görünür/yüklü satır anahtarları (sıralı) — Previous/Next gezinme için.</summary>
    System.Collections.Generic.IReadOnlyList<object> GridVisibleKeys { get; }

    /// <summary>Sunucudaki TOPLAM kayıt (anlık, grid'in son fetch'inden) — sayfa-aşırı gezinme üst sınırı.</summary>
    long TotalCount { get; }

    /// <summary>Yüklü sayfanın SkipCount'u (anlık) — global index = PageSkip + yerel index.</summary>
    int PageSkip { get; }

    /// <summary>Verilen anahtarlı satıra grid odağını taşı + görünür yap (Previous/Next senkronu / seçili satır highlight).</summary>
    Task FocusDataItemAsync(object? id);

    /// <summary>Sayfa-aşırı gezinme: verilen global index'in bulunduğu sayfayı grid'e yükle (PageIndex async +
    /// satır yüklenmesini bekle) ve o satırın Id'sini döndür. Komşu kayıt yüklü sayfa dışındaysa kullanılır.</summary>
    Task<object?> EnsurePageForGlobalIndexAsync(int globalIndex);
}

/// <summary>
/// Edit aksiyonları — hem split panelinin hem standalone edit formunun (popup/sekme) merkezi
/// CrudToolbar'a sunduğu sözleşme. CrudEditComponentBase implement eder.
/// </summary>
public interface ISplitEditActions
{
    bool CanSave { get; }
    bool IsNew { get; }

    /// <summary>Salt-okunur mod (ör. tenant'ta global/host kaydı). Kaydet/Sil gizlenir, form devre dışı,
    /// üstte bilgilendirme banner'ı gösterilir.</summary>
    bool IsReadOnly { get; }

    /// <summary>Salt-okunur formda gösterilecek bilgilendirme metni; null ise genel mesaj kullanılır.</summary>
    string? ReadOnlyNotice { get; }

    Task SaveAsync();
    Task SaveAndNewAsync();
    Task SaveAndCloseAsync();

    /// <summary>Edit modunda Sil: açık kaydı siler. Yeni kayıtta CanDelete=false.</summary>
    bool CanDelete { get; }
    Task DeleteAsync();

    /// <summary>Standalone/popup edit'te merkezi StateService listesinde önceki/sonraki kayda geç (guard'lı).
    /// Split'te bu yol kullanılmaz; SplitHost.GoPrevious/Next devrededir.</summary>
    bool CanGoPrevious { get; }
    bool CanGoNext { get; }
    Task GoPreviousAsync();
    Task GoNextAsync();

    /// <summary>
    /// Panelden ayrılmak (başka kayda geçiş / Geri) güvenli mi?
    /// Temizse true; kirliyse kullanıcıya discard onayı sorar (XAF ConfirmationRequest karşılığı).
    /// </summary>
    Task<bool> CanLeaveAsync();

    // ── Reset / Undo / Redo ──
    /// <summary>Kaydedilmemiş değişiklikleri at, orijinali yeniden yükle.</summary>
    Task ResetAsync();
    bool CanUndo { get; }
    bool CanRedo { get; }
    Task UndoAsync();
    Task RedoAsync();

    // ── Form değişiklik sinyalleri (DOM event delegation) ──
    // DevExpress editörleri @bind ile EditModel'i değiştiriyor ama EditContext.NotifyFieldChanged
    // ÇAĞIRMIYOR → OnFieldChanged ölü. Bu yüzden formu saran div'in oninput/onchange'i kullanılır.
    /// <summary>Herhangi bir editörde değişiklik (her tuş) — dirty/toolbar durumunu anlık tazele.</summary>
    void NotifyInput();
    /// <summary>Editör commit (blur/change) — undo geçmişine bir adım ekle.</summary>
    void CommitUndoStep();
}
