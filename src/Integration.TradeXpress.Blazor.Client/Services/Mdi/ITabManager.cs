using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

/// <summary>Açık MDI sekmelerinin tek sahibi (WASM tek kullanıcı → Singleton). NavMenu, MdiTabHost
/// ve sayfa-içi drill kodu aynı koleksiyonu paylaşır.</summary>
public interface ITabManager
{
    IReadOnlyList<MdiTab> Tabs { get; }
    Guid? ActiveTabId { get; }
    bool HasDirtyTabs { get; }
    event Action? StateChanged;

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
    void Refresh(Guid id);
    Task ReloadTabsAsync();
    Task HardResetAsync();
    void UpdateTabUrl(Guid tabId, string url);
}
