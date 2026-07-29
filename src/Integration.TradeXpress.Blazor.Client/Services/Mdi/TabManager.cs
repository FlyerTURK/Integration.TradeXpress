using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Settings;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.UI.Navigation;

namespace Integration.TradeXpress.Blazor.Client.Services.Mdi;

public sealed class TabManager : ITabManager, IMdiTabOpener
{
    private readonly RouteResolver _resolver;
    private readonly IUserUiSettingAppService _uiSettings;
    private readonly Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService _working;
    private readonly ILogger<TabManager> _logger;
    private readonly IStringLocalizer<TradeXpressResource> _localizer;
    private readonly IMenuManager _menuManager;
    private readonly List<MdiTab> _tabs = new();
    private Guid? _activeId;

    /// <summary>Paylaşılan init Task'ı — eşzamanlı ikinci çağıran AYNI yüklemeyi bekler (boş sekme penceresi yok).</summary>
    private Task? _initTask;

    /// <summary>Reload/rehydrate penceresinde tetiklenen Persist'leri bastırır — geçici boş/yarım liste
    /// kalıcı duruma yazılmasın.</summary>
    private bool _suspendPersist;

    public TabManager(
        RouteResolver resolver,
        IUserUiSettingAppService uiSettings,
        Integration.TradeXpress.Blazor.Client.Services.Working.IWorkingContextService working,
        ILogger<TabManager> logger,
        IStringLocalizer<TradeXpressResource> localizer,
        IMenuManager menuManager)
    {
        _resolver = resolver;
        _uiSettings = uiSettings;
        _working = working;
        _logger = logger;
        _localizer = localizer;
        _menuManager = menuManager;
    }

    public IReadOnlyList<MdiTab> Tabs => _tabs;
    public Guid? ActiveTabId => _activeId;
    public bool HasDirtyTabs => _tabs.Any(t => t.IsDirty);
    public event Action? StateChanged;
    public event Action? RestoreFailed;
    public event Action? PersistFailed;

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
        // Working context'in yüklenmesi beklenir (paylaşılan Task — ek maliyet yok): saklı şube geçersizse
        // WorkingContextService onu düzeltip sunucu ayarına persist eder; sunucu-tarafı anahtar çözümleme
        // (GetMdiTabsAsync) bu düzeltmeden SONRA doğru kovayı okur. try/catch İLE: bu, RehydrateAsync'in
        // kendi try/catch'inin (bozuk kayıt → "boş liste + toast") DIŞINDA kalan bir çağrıdır — sarmalanmazsa
        // şube/kasa listesi API'sindeki geçici bir hata (ağ/timeout) MdiTabHost.OnAfterRenderAsync'e kadar
        // yakalanmadan yayılıp tüm kabuğu (AutoRecoverErrorBoundary) düşürürdü. GetMdiTabsAsync bucket'ı
        // SUNUCUDAKİ working-branch ayarından çözer (ABP setting) — client _working state'i başarısız
        // yüklense bile Rehydrate yine doğru çalışır.
        try
        {
            await _working.EnsureLoadedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Çalışma bağlamı (şube/kasa) yüklenemedi — sekmeler yine de geri yüklenmeye çalışılacak.");
        }
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
        _suspendPersist = true;
        try
        {
            _tabs.Clear();
            _activeId = null;
            await RehydrateAsync();
        }
        finally
        {
            _suspendPersist = false;
        }
        Raise();
    }

    public async Task HardResetAsync()
    {
        // InitializeCoreAsync'teki desenle tutarlı: saklı şube geçersizse EnsureLoadedAsync onu düzeltip
        // sunucuya persist etsin — aksi halde /reset-tabs (bozuk-durum kurtarma kapısı, TAM DA bunun gibi
        // bir yarışın olacağı an) "[]"i bayat/yanlış şube kovasına yazabilir ve sıfırlama etkisiz kalırdı.
        try
        {
            await _working.EnsureLoadedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sekme sıfırlama öncesi çalışma bağlamı yüklenemedi — mevcut bağlamla devam ediliyor.");
        }

        _tabs.Clear();
        _activeId = null;
        await _uiSettings.SetMdiTabsAsync("[]");
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

    public void TogglePin(Guid id)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == id);
        if (tab == null) return;
        tab.IsPinned = !tab.IsPinned;
        Persist(); Raise();   // AllowClose + toplu-kapatma muafiyetleri IsPinned'i zaten okur.
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
        Raise();      // SplitView liste tab'ı: düz Title'a "*" (Header'a dokunmadan).
        Persist();    // kirli sekmeler de kalıcılaştırıldığından WasDirty güncel yazılmalı.
    }

    public void UpdateTabUrl(Guid tabId, string url)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || string.Equals(tab.Url, url, StringComparison.OrdinalIgnoreCase)) return;
        tab.Url = url;
        Persist();
    }

    public void UpdateTabState(Guid tabId, string? stateJson)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.PageState == stateJson) return;
        tab.PageState = stateJson;
        Persist();   // render'a gerek yok (görünüm zaten sayfada) — yalnız kalıcılaştır.
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

    // ---- Kalıcılık (AppUserGridLayouts; anahtar sunucuda çözülür — UserUiSettingAppService) ----
    // NOT (bilinçli sınırlama): aynı kullanıcının iki tarayıcı penceresi = iki circuit = aynı kayda yazar;
    // son yazan kazanır. SavedAt ileride "başka pencerede değişti" tespiti için saklanır — tam senkron
    // maliyetine değmez (kayıp yalnız sekme listesi, form verisi değil).

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
        string? ParentValue = null,
        // Sekme kaydedilirken kirliydi: restore'da sekme TEMİZ açılır (form verisi geri getirilemez),
        // ama kullanıcıya "kaydedilmemiş değişiklikler kayboldu" bilgisi verilir.
        bool WasDirty = false,
        // Sayfa-içi görünüm durumu (JSON) — TabPageState sözleşmesi (yalnız görünüm/filtre/kimlik).
        string? PageState = null,
        // Lokalizasyon ANAHTARLARI — restore'da başlık güncel kültürle çözülür (dil değişiminde donmaz);
        // çevrilmiş metinler yine saklanır (anahtarsız eski kayıt + çözülemeyen anahtar fallback'i).
        string? FormCaptionKey = null,
        string? ParentLabelKey = null);
    private sealed record PersistedState(
        List<PersistedTab> Tabs,
        string? ActiveUrl,
        int? ActiveIndex = null,
        DateTimeOffset? SavedAt = null);

    // ── Persist coalescing: fire-and-forget çağrılar yarışmasın diye tek kuyruk ──
    // Aktif persist sürerken gelen istek yalnız 'dirty' işaretler; aktif tur bitince güncel durum
    // BİR kez daha yazılır → "son istenen durum kazanır", eski state yeniyi ezemez.
    // Circuit dispatcher tek-thread olduğundan bayraklar await'ler arasında güvenli; SemaphoreSlim(1,1)
    // yalnız "aktif persist var mı" kapısı (lock değil — await-düzeni korunur, dispatcher bloklanmaz).
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private bool _persistDirty;

    private void Persist()
    {
        if (_suspendPersist) return;   // reload penceresi — geçici liste yazılmasın
        _ = PersistCoalescedAsync();
    }

    private async Task PersistCoalescedAsync()
    {
        _persistDirty = true;
        if (!await _persistGate.WaitAsync(0))
            return; // aktif persist var — dirty işaretlendi, o tur bitince son durumu yazacak

        try
        {
            while (_persistDirty)
            {
                // Debounce: art arda gelen güncellemeler (özellikle PageState — her filtre/tarih değişimi)
                // tek yazıma birleşsin; 300 ms içinde gelenler aynı turda serileştirilir.
                await Task.Delay(300);
                _persistDirty = false;

                // _suspendPersist yalnız Persist() GİRİŞİNDE kontrol edilirdi — kuyruğa girmiş bir tur
                // ReloadTabsAsync'in Clear()+Rehydrate penceresinde (bayrak sonradan true olur) uyanırsa
                // bu kontrol olmadan BOŞ/yarım _tabs sunucuya yazılırdı; anahtar YAZIM ANINDA working-branch'ten
                // çözüldüğünden (UserUiSettingAppService) bu, YENİ şubenin kovasını ezerdi. Circuit dispatcher
                // tek-thread'li olduğundan burada okunan değer güncel — bu turu sessizce atlamak güvenli:
                // ReloadTabsAsync zaten kendi rehydrate'iyle doğru durumu kuracak, bu turun verisi bayattır.
                if (_suspendPersist) continue;

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
            var activeIndex = _activeId != null ? _tabs.FindIndex(t => t.Id == _activeId) : -1;
            var state = new PersistedState(
                // Kirli sekmeler de yazılır: sekme F5/kasa değişiminde KAYBOLMAZ; yalnız form verisi
                // geri getirilemez (WasDirty bayrağı restore'da kullanıcıya bildirilir).
                _tabs.Select(t => new PersistedTab(
                    t.Url, t.Title, t.IconCssClass ?? t.Header?.IconCssClass,
                    t.Kind == TabKind.External, t.IsPinned,
                    t.Header?.FormCaption, t.Header?.EntityValue,
                    t.Header?.ParentLabel, t.Header?.ParentValue,
                    WasDirty: t.IsDirty,
                    PageState: t.PageState,
                    FormCaptionKey: t.Header?.FormCaptionKey,
                    ParentLabelKey: t.Header?.ParentLabelKey)).ToList(),
                active,
                activeIndex >= 0 ? activeIndex : null,
                DateTimeOffset.UtcNow);

            await _uiSettings.SetMdiTabsAsync(JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MDI sekme durumu kalıcılaştırılamadı.");
            PersistFailed?.Invoke();
        }
    }

    private async Task RehydrateAsync()
    {
        try
        {
            var json = await _uiSettings.GetMdiTabsAsync();
            if (string.IsNullOrWhiteSpace(json) || json == "[]") return;

            var state = JsonSerializer.Deserialize<PersistedState>(json);
            if (state?.Tabs == null) return;

            var lostDirtyCount = 0;
            MdiTab? activeByIndex = null;

            for (var i = 0; i < state.Tabs.Count; i++)
            {
                var p = state.Tabs[i];
                if (p.Url == "/") continue;   // eski kaldırılmış Home tab izi — restore etme (stale persisted state temizliği)

                MdiTab? added = null;
                if (p.IsExternal)
                {
                    added = new MdiTab { Title = p.Title, IconCssClass = p.Icon, Kind = TabKind.External, Url = p.Url, IsPinned = p.IsPinned };
                    _tabs.Add(added);
                }
                else
                {
                    var match = _resolver.Match(p.Url);
                    if (match == null) continue; // route artık yok → atla

                    // Kaydedilmiş header alanları varsa hemen restore et → bileşen yüklenene kadar tab şeridinde görünür.
                    // Lokalizasyon anahtarı taşıyan alanlar GÜNCEL kültürle yeniden çözülür (dil değişiminde donmaz);
                    // anahtar yoksa/çözülemiyorsa saklı çevrilmiş metin kullanılır (eski kayıt geri uyumu).
                    TabHeaderData? restoredHeader = null;
                    if (!string.IsNullOrEmpty(p.FormCaption))
                        restoredHeader = new TabHeaderData
                        {
                            FormCaption = ResolveLocalized(p.FormCaptionKey, p.FormCaption)!,
                            EntityValue = p.EntityValue,
                            ParentLabel = ResolveLocalized(p.ParentLabelKey, p.ParentLabel),
                            ParentValue = p.ParentValue,
                            IconCssClass = p.Icon,
                            FormCaptionKey = p.FormCaptionKey,
                            ParentLabelKey = p.ParentLabelKey,
                        };

                    added = new MdiTab
                    {
                        Title = p.Title,
                        IconCssClass = p.Icon,
                        Kind = TabKind.Internal,
                        Url = p.Url,
                        PageType = match.PageType,
                        Parameters = match.Parameters,
                        IsPinned = p.IsPinned,
                        Header = restoredHeader,
                        PageState = p.PageState,
                    };
                    _tabs.Add(added);

                    // Kirliyken kaydedilmiş sekme temiz açılır; kullanıcıya toplam sayıyla bildirilir.
                    if (p.WasDirty) lostDirtyCount++;
                }

                // Aktiflik index'le eşlenir (persisted listedeki sıra) — aynı URL'li/retarget edilmiş
                // sekmelerde URL eşleşmesinin kayma riski yok. Atlanan kayıtlar index'i bozamaz çünkü
                // eşleme persisted index üzerinden yapılır.
                if (state.ActiveIndex == i) activeByIndex = added;
            }

            var act = activeByIndex
                ?? (state.ActiveUrl != null   // eski format kayıtlar (ActiveIndex yok) için URL fallback'i
                    ? _tabs.FirstOrDefault(t => string.Equals(t.Url, state.ActiveUrl, StringComparison.OrdinalIgnoreCase))
                    : null);
            _activeId = act?.Id ?? _tabs.FirstOrDefault()?.Id;

            // Menüden açılmış sekmelerin başlık/ikonu GÜNCEL kültürle tazelenir (Title çevrilmiş metin
            // olarak saklandığından dil değişimi sonrası donuk kalıyordu). Menü her circuit'te güncel
            // kültürle lokalize gelir — kayıt formatı değişmeden ana dil-dayanıklılık kazancı budur.
            await RefreshTitlesFromMenuAsync();

            // 2026-07-28 Hakan: bu durum artık kullanıcıya TOAST'la bildirilmiyor. Gerekçesi: uyarı her
            // oturum açılışında yeniden çıkıyor ve elde edilecek bir şey yok — form verisi zaten gitmiş,
            // kullanıcının yapabileceği bir işlem kalmamış. Bilgi yine de KAYBOLMASIN diye log'a düşer.
            if (lostDirtyCount > 0)
            {
                _logger.LogInformation(
                    "MDI sekme geri yüklemesi: {LostDirtyCount} sekme kirliyken kaydedilmişti, temiz açıldı.",
                    lostDirtyCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MDI sekme durumu geri yüklenemedi — boş sekme listesiyle devam ediliyor.");
            RestoreFailed?.Invoke();
        }
    }

    /// <summary>Internal sekmelerin path'i ana menü URL'leriyle eşleştirilir; eşleşende Title + ikon menüden
    /// (güncel kültür) tazelenir. Header'lı sekmelerde görüntü Header'dan geldiği için etkisi yalnız
    /// Header'sız (düz menü/liste) sekmelerdedir. Hata kritik değildir — saklı başlıklar kalır.</summary>
    private async Task RefreshTitlesFromMenuAsync()
    {
        try
        {
            var menu = await _menuManager.GetAsync(StandardMenus.Main);
            var byUrl = new Dictionary<string, ApplicationMenuItem>(StringComparer.OrdinalIgnoreCase);
            CollectMenuUrls(menu.Items, byUrl);

            foreach (var tab in _tabs)
            {
                if (tab.Kind != TabKind.Internal) continue;
                var path = Normalize(tab.Url.Split('?')[0]);
                if (byUrl.TryGetValue(path, out var item))
                {
                    tab.Title = item.DisplayName;
                    if (!string.IsNullOrEmpty(item.Icon)) tab.IconCssClass = item.Icon;

                    // Menüden açılan sekmeler de YAPISAL Header taşır (OpenOrActivateAsync string overload'u
                    // TabHeaderData kurar) ve şeritteki görünüm Header.FormCaption'dan gelir — yalnız Title'ı
                    // tazelemek yetmez. Kayıt kimliği taşımayan (EntityValue/ParentValue boş) ve kendi
                    // lokalizasyon anahtarı olmayan header, menünün güncel dilindeki adla yeniden kurulur.
                    if (tab.Header is { } header
                        && string.IsNullOrEmpty(header.FormCaptionKey)
                        && string.IsNullOrEmpty(header.EntityValue)
                        && string.IsNullOrEmpty(header.ParentValue))
                    {
                        tab.Header = header with
                        {
                            FormCaption = item.DisplayName,
                            IconCssClass = string.IsNullOrEmpty(item.Icon) ? header.IconCssClass : item.Icon,
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sekme başlıkları menüden tazelenemedi — saklı başlıklarla devam ediliyor.");
        }
    }

    private static void CollectMenuUrls(IReadOnlyList<ApplicationMenuItem> items, Dictionary<string, ApplicationMenuItem> byUrl)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Url))
                byUrl.TryAdd(Normalize(item.Url), item);
            if (item.Items.Count > 0)
                CollectMenuUrls(item.Items, byUrl);
        }
    }

    /// <summary>Anahtar varsa güncel kültürle çözer; anahtar yok/kaynakta bulunamıyorsa saklı metin döner.</summary>
    private string? ResolveLocalized(string? key, string? storedText)
    {
        if (string.IsNullOrEmpty(key)) return storedText;
        var localized = _localizer[key];
        return localized.ResourceNotFound ? storedText : localized.Value;
    }
}

