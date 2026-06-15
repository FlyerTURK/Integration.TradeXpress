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
    event Action? StateChanged;

    /// <summary>localStorage'dan sekmeleri geri yükler; boşsa varsayılan (Home) sekmesini açar. Bir kez çalışır.</summary>
    Task InitializeAsync(string defaultUrl, string defaultTitle, string? defaultIcon);

    /// <summary>Aynı URL'li sekme varsa aktive eder, yoksa çözümleyip yeni iç sekme açar. Çözümlenemezse no-op.</summary>
    Task OpenOrActivateAsync(string url, string title, string? icon = null);

    void OpenExternal(string url, string title, string? icon = null);
    void Activate(Guid id);
    void Close(Guid id);
    void CloseOthers(Guid id);
    void CloseAll();
    void CloseToRight(Guid id);
    void Refresh(Guid id);
}
