using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

public sealed class TabManager : ITabManager
{
    private const string StorageKey = "tx.mdi.tabs";

    private readonly RouteResolver _resolver;
    private readonly IJSRuntime _js;
    private readonly List<MdiTab> _tabs = new();
    private Guid? _activeId;
    private bool _initialized;

    public TabManager(RouteResolver resolver, IJSRuntime js)
    {
        _resolver = resolver;
        _js = js;
    }

    public IReadOnlyList<MdiTab> Tabs => _tabs;
    public Guid? ActiveTabId => _activeId;
    public event Action? StateChanged;

    public async Task InitializeAsync(string defaultUrl, string defaultTitle, string? defaultIcon)
    {
        if (_initialized) return;
        _initialized = true;

        await RehydrateAsync();

        if (_tabs.Count == 0)
            await OpenOrActivateAsync(defaultUrl, defaultTitle, defaultIcon);
        else
            Raise();
    }

    public Task OpenOrActivateAsync(string url, string title, string? icon = null)
    {
        url = Normalize(url);

        var existing = _tabs.FirstOrDefault(t =>
            t.Kind == TabKind.Internal && string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _activeId = existing.Id;
            Persist(); Raise();
            return Task.CompletedTask;
        }

        var match = _resolver.Match(url);
        if (match == null) return Task.CompletedTask; // bilinmeyen iç route → NavMenu fallback eder

        var tab = new MdiTab
        {
            Title = title,
            IconCssClass = icon,
            Kind = TabKind.Internal,
            Url = url,
            PageType = match.PageType,
            Parameters = match.Parameters,
            IsPinned = url == "/",
        };
        _tabs.Add(tab);
        _activeId = tab.Id;
        Persist(); Raise();
        return Task.CompletedTask;
    }

    public void OpenExternal(string url, string title, string? icon = null)
    {
        var existing = _tabs.FirstOrDefault(t =>
            t.Kind == TabKind.External && string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { _activeId = existing.Id; Persist(); Raise(); return; }

        var tab = new MdiTab
        {
            Title = title,
            IconCssClass = icon ?? "fas fa-globe",
            Kind = TabKind.External,
            Url = url,
        };
        _tabs.Add(tab);
        _activeId = tab.Id;
        Persist(); Raise();
    }

    public void Activate(Guid id)
    {
        if (_tabs.Any(t => t.Id == id)) { _activeId = id; Persist(); Raise(); }
    }

    public void Close(Guid id)
    {
        var idx = _tabs.FindIndex(t => t.Id == id);
        if (idx < 0) return;

        var wasActive = _activeId == id;
        _tabs.RemoveAt(idx);
        if (wasActive)
            _activeId = _tabs.Count == 0 ? null : _tabs[Math.Min(idx, _tabs.Count - 1)].Id;

        Persist(); Raise();
    }

    public void CloseOthers(Guid id)
    {
        _tabs.RemoveAll(t => t.Id != id && !t.IsPinned);
        _activeId = _tabs.Any(t => t.Id == id) ? id : _tabs.FirstOrDefault()?.Id;
        Persist(); Raise();
    }

    public void CloseAll()
    {
        _tabs.RemoveAll(t => !t.IsPinned);
        _activeId = _tabs.FirstOrDefault()?.Id;
        Persist(); Raise();
    }

    public void CloseToRight(Guid id)
    {
        var idx = _tabs.FindIndex(t => t.Id == id);
        if (idx < 0) return;

        for (int i = _tabs.Count - 1; i > idx; i--)
            if (!_tabs[i].IsPinned) _tabs.RemoveAt(i);

        if (_activeId != null && _tabs.All(t => t.Id != _activeId))
            _activeId = _tabs[idx].Id;

        Persist(); Raise();
    }

    public void Refresh(Guid id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab == null) return;
        tab.RefreshNonce = Guid.NewGuid();
        Raise();
    }

    private void Raise() => StateChanged?.Invoke();

    private static string Normalize(string url)
    {
        url = url?.Trim() ?? "/";
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url;
        if (!url.StartsWith('/')) url = "/" + url;
        return url;
    }

    // ---- Kalıcılık (localStorage) — dil değişimi forceLoad reload'ı sekmeleri silmesin diye ----

    private sealed record PersistedTab(string Url, string Title, string? Icon, bool IsExternal, bool IsPinned);
    private sealed record PersistedState(List<PersistedTab> Tabs, string? ActiveUrl);

    private void Persist() => _ = PersistAsync();

    private async Task PersistAsync()
    {
        try
        {
            var active = _activeId != null ? _tabs.FirstOrDefault(t => t.Id == _activeId)?.Url : null;
            var state = new PersistedState(
                _tabs.Select(t => new PersistedTab(t.Url, t.Title, t.IconCssClass, t.Kind == TabKind.External, t.IsPinned)).ToList(),
                active);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, JsonSerializer.Serialize(state));
        }
        catch { /* JS yok / disconnected — sessiz geç */ }
    }

    private async Task RehydrateAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
            if (string.IsNullOrWhiteSpace(json)) return;

            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state?.Tabs == null) return;

            foreach (var p in state.Tabs)
            {
                if (p.IsExternal)
                {
                    _tabs.Add(new MdiTab { Title = p.Title, IconCssClass = p.Icon, Kind = TabKind.External, Url = p.Url, IsPinned = p.IsPinned });
                }
                else
                {
                    var match = _resolver.Match(p.Url);
                    if (match == null) continue; // route artık yok → atla
                    _tabs.Add(new MdiTab
                    {
                        Title = p.Title,
                        IconCssClass = p.Icon,
                        Kind = TabKind.Internal,
                        Url = p.Url,
                        PageType = match.PageType,
                        Parameters = match.Parameters,
                        IsPinned = p.IsPinned,
                    });
                }
            }

            var act = state.ActiveUrl != null
                ? _tabs.FirstOrDefault(t => string.Equals(t.Url, state.ActiveUrl, StringComparison.OrdinalIgnoreCase))
                : null;
            _activeId = act?.Id ?? _tabs.FirstOrDefault()?.Id;
        }
        catch { /* bozuk state → yoksay */ }
    }
}
