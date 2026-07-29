using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

/// <summary>Açık MDI sekmelerinin tek sahibi. Blazor Server'da SCOPED (circuit ömrü — her pencere ayrı
/// instance, kalıcı durum DB'de paylaşılır; son yazan kazanır), WASM'da Singleton. NavMenu, MdiTabHost
/// ve sayfa-içi drill kodu aynı koleksiyonu paylaşır.</summary>
public interface ITabManager
{
    IReadOnlyList<MdiTab> Tabs { get; }
    Guid? ActiveTabId { get; }
    bool HasDirtyTabs { get; }
    event Action? StateChanged;

    // Not: "restore edilen sekmelerden N tanesi kirliydi" olayı 2026-07-28'de KALDIRILDI — kullanıcıya
    // toast göstermenin karşılığı yoktu (veri zaten gitmiş, yapılacak işlem yok). Durum TabManager'da
    // loglanır; UI'a taşınmaz.

    /// <summary>Sekme geri yükleme başarısız oldu (bozuk kayıt / ağ) — boş listeyle devam ediliyor.</summary>
    event Action? RestoreFailed;

    /// <summary>Sekme durumu kaydedilemedi (ağ/sunucu hatası) — UI ilk hatada tek uyarı gösterir.</summary>
    event Action? PersistFailed;

    /// <summary>Veritabanından (AppUserGridLayouts) sekmeleri geri yükler; boşsa varsayılan (Home) sekmesini açar. Bir kez çalışır.</summary>
    Task InitializeAsync(string? defaultUrl, string? defaultTitle, string? defaultIcon);

    /// <summary>Aynı URL'li sekme varsa aktive eder, yoksa çözümleyip yeni iç sekme açar. Çözümlenemezse no-op.</summary>
    Task OpenOrActivateAsync(string url, string title, string? icon = null);

    /// <summary>TabHeaderData kullanarak sekme açar veya aktive eder. Bu sayede listelerde de yapısal başlık (EditHeaderView) gösterilebilir.</summary>
    Task OpenOrActivateAsync(string url, Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData headerData);

    /// <summary>Edit sayfası, sekmesinin yapısal başlığını (3-satır caption + dirty) günceller. Bilinmeyen id → no-op.</summary>
    void UpdateTabHeader(Guid tabId, TabHeaderData header);

    /// <summary>Düz sekme başlığını (Title) günceller — ör. cari seçilince sekme adı hesap olur. Bilinmeyen id → no-op.</summary>
    void SetTabTitle(Guid tabId, string title);

    /// <summary>SplitView'da embedded edit, liste tab'ının başlığını ezmeden sadece dirty bayrağını set eder.</summary>
    void SetTabDirty(Guid tabId, bool isDirty);

    void OpenExternal(string url, string title, string? icon = null);
    void Activate(Guid id);
    void Close(Guid id);
    Task<bool> TryCloseAsync(Guid id);
    void CloseOthers(Guid id);
    void CloseAll();
    void CloseToRight(Guid id);

    /// <summary>Toplu-kapatma kapsamı için hedef sekmeler (pinned HARİÇ) — UI dirty kontrolü + tek-uyarı kararı için.</summary>
    IReadOnlyList<MdiTab> GetCloseTargets(TabCloseScope scope, Guid anchorId);
    /// <summary>Verilen id'leri FORCE kapatır (guard YOK — UI zaten karar verdi: ör. "yine de kapat"/"kaydedilmişleri kapat").
    /// Pinned atlanır. Tek Persist/Raise.</summary>
    void CloseMany(IEnumerable<Guid> ids);

    void Refresh(Guid id);

    /// <summary>Sekmeyi sabitler / sabitlemeyi kaldırır. Sabit sekme: X butonu gizli (AllowClose=false)
    /// ve tüm toplu kapatmalardan muaf.</summary>
    void TogglePin(Guid id);

    Task ReloadTabsAsync();
    Task HardResetAsync();
    void UpdateTabUrl(Guid tabId, string url);

    /// <summary>Sekmenin sayfa-içi görünüm durumunu (JSON) günceller ve kalıcılaştırır — restore'da
    /// <see cref="MdiTab.PageState"/> üzerinden geri okunur. null = temizle. Bilinmeyen id → no-op.</summary>
    void UpdateTabState(Guid tabId, string? stateJson);
}

/// <summary>Toplu sekme kapatma kapsamı (sağ-tık context menüsü).</summary>
public enum TabCloseScope
{
    /// <summary>Anchor hariç tüm (pinned olmayan) sekmeler.</summary>
    Others,
    /// <summary>Anchor'ın sağındaki (pinned olmayan) sekmeler.</summary>
    ToRight,
    /// <summary>Tüm (pinned olmayan) sekmeler.</summary>
    All
}
