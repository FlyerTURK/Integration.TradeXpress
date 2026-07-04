using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Settings;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

public sealed class TabManager : ITabManager, IMdiTabOpener
{
    private readonly RouteResolver _resolver;
    private readonly IUserUiSettingAppService _uiSettings;
    private readonly Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService _working;
    private readonly List<MdiTab> _tabs = new();
    private Guid? _activeId;

    /// <summary>Paylaşılan init Task'ı — eşzamanlı ikinci çağıran AYNI yüklemeyi bekler (boş sekme penceresi yok).</summary>
    private Task? _initTask;

    public TabManager(RouteResolver resolver, IUserUiSettingAppService uiSettings, Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService working)
    {
        _resolver = resolver;
        _uiSettings = uiSettings;
        _working = working;
    }

    public IReadOnlyList<MdiTab> Tabs => _tabs;
    public Guid? ActiveTabId => _activeId;
    public bool HasDirtyTabs => _tabs.Any(t => t.IsDirty);
    public event Action? StateChanged;

    public async Task InitializeAsync(string? defaultUrl, string? defaultTitle, string? defaultIcon)
    {
        // Paylaşılan Task deseni: bayrağı await'ten önce set etmek yerine Task'ın kendisi paylaşılır —
        // ikinci çağıran yükleme bitmeden dönmez (boş-liste yarış penceresi kapanır).
        var task = _initTask ??= InitializeCoreAsync(defaultUrl, defaultTitle, defaultIcon);
        try
        {
            await task;
        }
        catch
        {
            // Başarısız init'te Task sıfırlanır → sonraki çağrı yeniden dener (kalıcı bozuk durumda kalma).
            if (_initTask == task) _initTask = null;
            throw;
        }
    }

    private async Task InitializeCoreAsync(string? defaultUrl, string? defaultTitle, string? defaultIcon)
    {
        await RehydrateAsync();

        if (_tabs.Count == 0)
        {
            if (!string.IsNullOrEmpty(defaultUrl))
                await OpenOrActivateAsync(defaultUrl, defaultTitle ?? "", defaultIcon);
        }
        else
            Raise();
    }

    public async Task ReloadTabsAsync()
    {
        _tabs.Clear();
        _activeId = null;
        await RehydrateAsync();
        Raise();
    }

    public async Task HardResetAsync()
    {
        _tabs.Clear();
        _activeId = null;
        var branchId = _working.CurrentBranchId?.ToString();
        var key = string.IsNullOrEmpty(branchId) ? "MdiTabs" : $"MdiTabs_{branchId}";
        await _uiSettings.SetGridStateAsync(key, "[]");
        Raise();
    }

    public Task OpenOrActivateAsync(string url, string title, string? icon = null)
    {
        return OpenOrActivateAsync(url, new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData { FormCaption = title, IconCssClass = icon });
    }

    public Task OpenOrActivateAsync(string url, Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData headerData)
    {
        url = Normalize(url);

        var existing = _tabs.FirstOrDefault(t =>
            t.Kind == TabKind.Internal && string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            _activeId = existing.Id;
            existing.Header = headerData;
            existing.Title = headerData.FormCaption;
            existing.IconCssClass = headerData.IconCssClass;
            Persist(); Raise();
            return Task.CompletedTask;
        }

        var match = _resolver.Match(url);
        if (match == null) return Task.CompletedTask; // bilinmeyen iç route → NavMenu fallback eder

        var tab = new MdiTab
        {
            Title = headerData.FormCaption,
            Header = headerData,
            IconCssClass = headerData.IconCssClass,
            Kind = TabKind.Internal,
            Url = url,
            PageType = match.PageType,
            Parameters = match.Parameters,
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
            IconCssClass = icon ?? "custom-icon-country",
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

    public async Task<bool> TryCloseAsync(Guid id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab == null) return false;

        if (tab.CanCloseAsync != null)
        {
            var canClose = await tab.CanCloseAsync();
            if (!canClose) return false;
        }

        Close(id);
        return true;
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

    // ── Toplu kapatma: hedef hesabı (pinned hariç) + FORCE çoklu kapatma. Dirty kararını UI TEK uyarıda verir,
    //    sonra ya temizleri ya hepsini bu metoda gönderir (per-tab guard YOK — niyet zaten onaylandı). ──
    public IReadOnlyList<MdiTab> GetCloseTargets(TabCloseScope scope, Guid anchorId) => scope switch
    {
        TabCloseScope.Others  => _tabs.Where(t => t.Id != anchorId && !t.IsPinned).ToList(),
        TabCloseScope.All     => _tabs.Where(t => !t.IsPinned).ToList(),
        TabCloseScope.ToRight => RightOf(anchorId),
        _ => new List<MdiTab>()
    };

    private List<MdiTab> RightOf(Guid anchorId)
    {
        var idx = _tabs.FindIndex(t => t.Id == anchorId);
        return idx < 0 ? new List<MdiTab>() : _tabs.Skip(idx + 1).Where(t => !t.IsPinned).ToList();
    }

    public void CloseMany(IEnumerable<Guid> ids)
    {
        var set = new HashSet<Guid>(ids);
        if (set.Count == 0) return;

        _tabs.RemoveAll(t => set.Contains(t.Id) && !t.IsPinned);
        if (_activeId == null || _tabs.All(t => t.Id != _activeId))
            _activeId = _tabs.FirstOrDefault()?.Id;

        Persist(); Raise();
    }

    public void Refresh(Guid id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab == null) return;
        tab.RefreshNonce = Guid.NewGuid();
        Raise();
    }

    public void UpdateTabHeader(Guid tabId, TabHeaderData header)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Header == header) return;   // record value-eşitliği → gereksiz Raise/re-render yok
        tab.Header = header;
        // İkon güncel Header'dan al (icon sonradan değişebilir, ör. detaylı yüklemede).
        if (!string.IsNullOrEmpty(header.IconCssClass))
            tab.IconCssClass = header.IconCssClass;
        Raise(); Persist();   // strip + top-panel re-render. Header da kalıcılaştırılır → restore'da hemen görünür.
    }

    public void SetTabTitle(Guid tabId, string title)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Title == title) return;
        tab.Title = title;
        Raise();   // strip re-render. Kalıcılaştırma: persist sırasında güncel Title yazılır.
    }

    public void SetTabDirty(Guid tabId, bool isDirty)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.IsDirty == isDirty) return;
        tab.IsDirty = isDirty;
        Raise();   // SplitView liste tab'ı: düz Title'a "*" (Header'a dokunmadan).
    }

    public void UpdateTabUrl(Guid tabId, string url)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || string.Equals(tab.Url, url, StringComparison.OrdinalIgnoreCase)) return;
        tab.Url = url;
        Persist();
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

    // ---- Kalıcılık (ABP User Settings) ----

    private sealed record PersistedTab(
        string Url,
        string Title,
        string? Icon,
        bool IsExternal,
        bool IsPinned,
        // Header alanları — tab strip restore'da hemen görünsün (bileşen yüklenince zaten override edilir)
        string? FormCaption = null,
        string? EntityValue = null,
        string? ParentLabel = null,
        string? ParentValue = null);
    private sealed record PersistedState(List<PersistedTab> Tabs, string? ActiveUrl);

    // ── Persist coalescing: fire-and-forget çağrılar yarışmasın diye tek kuyruk ──
    // Aktif persist sürerken gelen istek yalnız 'dirty' işaretler; aktif tur bitince güncel durum
    // BİR kez daha yazılır → "son istenen durum kazanır", eski state yeniyi ezemez.
    // Circuit dispatcher tek-thread olduğundan bayraklar await'ler arasında güvenli; SemaphoreSlim(1,1)
    // yalnız "aktif persist var mı" kapısı (lock değil — await-düzeni korunur, dispatcher bloklanmaz).
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private bool _persistDirty;

    private void Persist() => _ = PersistCoalescedAsync();

    private async Task PersistCoalescedAsync()
    {
        _persistDirty = true;
        if (!await _persistGate.WaitAsync(0))
            return; // aktif persist var — dirty işaretlendi, o tur bitince son durumu yazacak

        try
        {
            while (_persistDirty)
            {
                _persistDirty = false;
                await PersistAsync(); // her turda GÜNCEL _tabs/_activeId serileştirilir
            }
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            var active = _activeId != null ? _tabs.FirstOrDefault(t => t.Id == _activeId)?.Url : null;
            var state = new PersistedState(
                _tabs.Where(t => !t.IsDirty).Select(t => new PersistedTab(
                    t.Url, t.Title, t.IconCssClass ?? t.Header?.IconCssClass,
                    t.Kind == TabKind.External, t.IsPinned,
                    t.Header?.FormCaption, t.Header?.EntityValue,
                    t.Header?.ParentLabel, t.Header?.ParentValue)).ToList(),
                active);
                
            var branchId = _working.CurrentBranchId?.ToString();
            var key = string.IsNullOrEmpty(branchId) ? "MdiTabs" : $"MdiTabs_{branchId}";
            await _uiSettings.SetGridStateAsync(key, JsonSerializer.Serialize(state));
        }
        catch { /* Network err — sessiz geç */ }
    }

    private async Task RehydrateAsync()
    {
        try
        {
            var branchId = _working.CurrentBranchId?.ToString();
            var key = string.IsNullOrEmpty(branchId) ? "MdiTabs" : $"MdiTabs_{branchId}";
            var json = await _uiSettings.GetGridStateAsync(key);
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return;

            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state?.Tabs == null) return;

            foreach (var p in state.Tabs)
            {
                if (p.Url == "/") continue;   // eski kaldırılmış Home tab izi — restore etme (stale persisted state temizliği)
                if (p.IsExternal)
                {
                    _tabs.Add(new MdiTab { Title = p.Title, IconCssClass = p.Icon, Kind = TabKind.External, Url = p.Url, IsPinned = p.IsPinned });
                }
                else
                {
                    var match = _resolver.Match(p.Url);
                    if (match == null) continue; // route artık yok → atla

                    // Kaydedilmiş header alanları varsa hemen restore et → bileşen yüklenene kadar tab şeridinde görünür.
                    TabHeaderData? restoredHeader = null;
                    if (!string.IsNullOrEmpty(p.FormCaption))
                        restoredHeader = new TabHeaderData
                        {
                            FormCaption = p.FormCaption,
                            EntityValue = p.EntityValue,
                            ParentLabel = p.ParentLabel,
                            ParentValue = p.ParentValue,
                            IconCssClass = p.Icon,
                        };

                    _tabs.Add(new MdiTab
                    {
                        Title = p.Title,
                        IconCssClass = p.Icon,
                        Kind = TabKind.Internal,
                        Url = p.Url,
                        PageType = match.PageType,
                        Parameters = match.Parameters,
                        IsPinned = p.IsPinned,
                        Header = restoredHeader,
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

