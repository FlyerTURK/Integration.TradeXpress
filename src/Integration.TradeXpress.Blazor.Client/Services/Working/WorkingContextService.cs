using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Settings;
using Integration.TradeXpress.Vaults;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

public interface IWorkingContextService
{
    IReadOnlyList<BranchListDto> Branches { get; }
    Guid? CurrentBranchId { get; }
    BranchListDto? CurrentBranch { get; }
    /// <summary>Seçili çalışma şubesinin şirketi (company-scoped sorgular için). Şube yoksa null.</summary>
    Guid? CurrentCompanyId { get; }

    /// <summary>
    /// Kullanıcının erişebildiği şirketler — server-side resolver-filtreli <see cref="Branches"/>'ten türer
    /// (sıralı, tekilleştirilmiş; ilk eleman = deterministik "ilk izinli"). Sunucu zorlaması
    /// (<c>WorkingCompanyContextProvider</c>) yetkisiz seçimi bu kümeye karşı doğrular.
    /// </summary>
    IReadOnlyList<Guid> AllowedCompanyIds { get; }

    /// <summary>Kullanıcının ÇALIŞABİLDİĞİ kasalar (server-side <c>GetMyVaultsAsync</c>) — working-context
    /// seçicisinin SATIRLARI. Çalışma bağlamı artık kasa hassasiyetindedir: şirket+şube+kasa.</summary>
    IReadOnlyList<MyVaultDto> Vaults { get; }

    Guid? CurrentVaultId { get; }
    MyVaultDto? CurrentVault { get; }

    bool IsLoaded { get; }

    event Action? Changed;

    Task EnsureLoadedAsync();
    Task SetBranchAsync(Guid? branchId);

    /// <summary>Çalışma KASASINI seçer — bağlamın tamamını (kasa + şubesi + şirketi) sürer. Seçici bunu çağırır.</summary>
    Task SetVaultAsync(Guid? vaultId);

    /// <summary>
    /// Şube/erişim kümesini sunucudan YENİDEN yükler (GetMyBranchesAsync). Grant runtime'da değişince
    /// (admin ScopedRoles'ü daralttı/genişletti) çağrılmalı: cache'li <see cref="Branches"/> ve türetilen
    /// <see cref="CurrentCompanyId"/> tazelenir; geçersizleşen seçim ilk izinli şubeye düşer. Tam
    /// invalidasyon-push yok — bu yeniden-yükleme yolu (RefreshAsync) yeterli (grant değişince tetiklenmeli).
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// Çalışma bağlamı (working context) — kullanıcının seçili çalışma KASASI (Company + Branch + Vault). Sol menü
/// footer'ındaki üç-kolonlu combo bunu sürer: satırlar KASA'dır, seçim kasanın şubesini ve şirketini de set eder. Kalıcılık SUNUCU TARAFINDA, per-user (ABP Setting Management
/// → AbpSettings, <see cref="IUserUiSettingAppService"/>): cihazdan bağımsız, kullanıcıyla taşınır. İlk
/// yüklemede saklı seçim hâlâ geçerliyse o, değilse İLK şube otomatik seçilir → combo asla boş kalmaz.
/// Scoped (Blazor Server circuit = kullanıcı oturumu). İleride kapsam (scoped) yetki filtrelemesi bunu okuyacak.
/// </summary>
public class WorkingContextService : IWorkingContextService
{
    private readonly IBranchAppService _branchAppService;
    private readonly IVaultAppService _vaultAppService;
    private readonly IUserUiSettingAppService _uiSettings;
    private readonly WorkingSelectionStore _selectionStore;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    private List<BranchListDto> _branches = new();
    private List<MyVaultDto> _vaults = new();
    private IReadOnlyList<Guid> _allowedCompanyIds = Array.Empty<Guid>();
    private Guid? _currentBranchId;
    private Guid? _currentVaultId;

    /// <summary>Paylaşılan yükleme Task'ı — eşzamanlı ikinci çağıran AYNI yüklemeyi bekler (boş şube listesi penceresi yok).</summary>
    private Task? _loadTask;

    public WorkingContextService(
        IBranchAppService branchAppService,
        IVaultAppService vaultAppService,
        IUserUiSettingAppService uiSettings,
        WorkingSelectionStore selectionStore,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _branchAppService = branchAppService;
        _vaultAppService = vaultAppService;
        _uiSettings = uiSettings;
        _selectionStore = selectionStore;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public IReadOnlyList<BranchListDto> Branches => _branches;
    public IReadOnlyList<MyVaultDto> Vaults => _vaults;
    public Guid? CurrentBranchId => _currentBranchId;
    public BranchListDto? CurrentBranch => _branches.FirstOrDefault(b => b.Id == _currentBranchId);
    public Guid? CurrentVaultId => _currentVaultId;
    public MyVaultDto? CurrentVault => _vaults.FirstOrDefault(v => v.Id == _currentVaultId);

    /// <summary>Seçili kasanın şirketi; kasa yoksa (fail-safe) eski davranış: seçili şubenin şirketi.</summary>
    public Guid? CurrentCompanyId => CurrentVault?.CompanyId ?? CurrentBranch?.CompanyId;
    public IReadOnlyList<Guid> AllowedCompanyIds => _allowedCompanyIds;
    public bool IsLoaded => _loadTask is { IsCompletedSuccessfully: true };

    public event Action? Changed;

    public async Task EnsureLoadedAsync()
    {
        // Paylaşılan Task deseni: bayrağı await'ten önce set etmek yerine Task'ın kendisi paylaşılır —
        // ikinci çağıran yükleme bitmeden dönmez (boş-liste yarış penceresi kapanır).
        var task = _loadTask ??= LoadCoreAsync();
        try
        {
            await task;
        }
        catch
        {
            // Başarısız yüklemede Task sıfırlanır → sonraki çağrı yeniden dener (kalıcı bozuk durumda kalma).
            if (_loadTask == task) _loadTask = null;
            throw;
        }
    }

    private async Task LoadCoreAsync()
    {
        // GetMyBranchesAsync = server-side kapsam (scope) daraltması (IScopedGrantResolver). Combo yalnız
        // kullanıcının erişebildiği şubeleri gösterir → seçim zaten izinli kümeyle sınırlı.
        var myBranches = await _branchAppService.GetMyBranchesAsync();
        _branches = myBranches
            .OrderBy(b => b.CompanyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // İzinli şirketler (sunucu zorlaması için): sıralı _branches'ten tekilleştir → deterministik ilk-izinli.
        // KASADAN TÜRETİLMEZ (bilinçli): "şubeye grant var, kasaya yok" kenar durumunda küme daralmamalı.
        _allowedCompanyIds = _branches.Select(b => b.CompanyId).Distinct().ToList();

        // Kasa seviyesi bağlam: seçicinin satırları (server-side kapsam daraltmalı).
        _vaults = await _vaultAppService.GetMyVaultsAsync();

        var storedBranch = await TryReadStoredBranchAsync();
        // Saklı seçim izinli kümede (server-filtreli _branches) hâlâ geçerliyse onu kullan; aksi halde
        // İLK İZİNLİ şube (combo boş kalmasın + yetkisiz saklı seçim izinli olana düşer).
        _currentBranchId = (storedBranch is { } s && _branches.Any(b => b.Id == s))
            ? storedBranch
            : _branches.FirstOrDefault()?.Id;

        // Saklı kasa hâlâ çalışabildiğim kasalardan biriyse onu kullan; değilse çalışma şubemin varsayılan
        // kasası, o da yoksa ilk kasa (combo boş kalmaz). Kasa hiç yoksa null → seçici render EDİLMEZ.
        var storedVault = await TryReadStoredVaultAsync();
        _currentVaultId = (storedVault is { } v && _vaults.Any(x => x.Id == v))
            ? storedVault
            : (_vaults.FirstOrDefault(x => x.BranchId == _currentBranchId) ?? _vaults.FirstOrDefault())?.Id;

        // Bağlam kasadan hizalanır: seçili kasanın şubesi çalışma şubesidir (şirket ondan türer).
        if (CurrentVault is { } current)
            _currentBranchId = current.BranchId;

        if (_currentBranchId != storedBranch)
            await PersistBranchAsync();
        if (_currentVaultId != storedVault)
            await PersistVaultAsync();

        PublishSelectionToStore();
        Changed?.Invoke();
    }

    /// <summary>Seçimi scope-bağımsız SSOT'a (singleton <see cref="WorkingSelectionStore"/>) yayınlar —
    /// UoW child scope'larındaki DbContext filtresi (WorkingCompanyContextProvider) bu değeri okur;
    /// circuit-scoped bu servisin boş kopyalarına düşmez (kök-neden fix'i).</summary>
    private void PublishSelectionToStore()
    {
        if (_currentUser.Id is { } userId)
        {
            _selectionStore.Set(
                _currentTenant.Id,
                userId,
                CurrentCompanyId,
                _allowedCompanyIds,
                _currentBranchId,
                _currentVaultId);
        }
    }

    /// <summary>
    /// Çalışma KASASINI seçer → bağlamın tamamını sürer: kasa + (kasanın) şubesi + şirketi. Seçim yalnız
    /// sunucu-filtreli <see cref="Vaults"/> kümesinden kabul edilir (erken UX guard; sunucu tarafı yetkiyi
    /// zaten kapsam-grant'i ile belirler). <c>WorkingBranch</c> ayarı yazılmaya devam eder — MDI sekme anahtarı odur.
    /// </summary>
    public async Task SetVaultAsync(Guid? vaultId)
    {
        if (_currentVaultId == vaultId) return;

        if (vaultId is not { } id || _vaults.FirstOrDefault(v => v.Id == id) is not { } vault)
            return;   // combo boş kalmasın: null / yetkisiz seçim yok sayılır

        _currentVaultId = id;

        var branchChanged = _currentBranchId != vault.BranchId;
        _currentBranchId = vault.BranchId;

        await PersistVaultAsync();
        if (branchChanged)
            await PersistBranchAsync();

        PublishSelectionToStore();
        Changed?.Invoke();
    }

    public async Task SetBranchAsync(Guid? branchId)
    {
        if (_currentBranchId == branchId) return;

        // Erken UX guard: yalnız izinli kümedeki (server-filtreli _branches) bir şube ya da null (temizle)
        // kabul edilir. Sunucu 2b'de (WorkingCompanyContextProvider) yine doğrular; bu, yetkisiz seçimi
        // combo düzeyinde reddeder (client'a güvenmenin ötesinde erken savunma).
        if (branchId is { } id && _branches.All(b => b.Id != id))
            return;

        _currentBranchId = branchId;
        await PersistBranchAsync();

        // Kasa bağlamını şubeyle hizala: mevcut kasa artık bu şubede değilse o şubedeki ilk (varsayılan)
        // kasaya geç — kasa listesi sıralıdır (varsayılan önce). Şubede kasam yoksa kasa bağlamı temizlenir.
        if (CurrentVault?.BranchId != _currentBranchId)
        {
            _currentVaultId = _vaults.FirstOrDefault(v => v.BranchId == _currentBranchId)?.Id;
            await PersistVaultAsync();
        }

        PublishSelectionToStore();
        Changed?.Invoke();
    }

    public Task RefreshAsync()
    {
        // Paylaşılan Task'ı sıfırla → EnsureLoadedAsync yeniden yükler (hata durumu / eşzamanlılık aynı
        // yerden yönetilir). Grant değişimi sonrası çağrılınca _branches + AllowedCompanyIds tazelenir.
        _loadTask = null;
        return EnsureLoadedAsync();
    }

    private async Task<Guid?> TryReadStoredBranchAsync()
    {
        try
        {
            var raw = await _uiSettings.GetWorkingBranchAsync();
            return Guid.TryParse(raw, out var g) ? g : null;
        }
        catch { return null; } // ayar okunamazsa sessiz → ilk şubeye düşer
    }

    private async Task<Guid?> TryReadStoredVaultAsync()
    {
        try
        {
            var raw = await _uiSettings.GetWorkingVaultAsync();
            return Guid.TryParse(raw, out var g) ? g : null;
        }
        catch { return null; } // ayar okunamazsa sessiz → varsayılan kasaya düşer
    }

    /// <summary>WorkingBranch ayarı kasa seviyesine geçtikten SONRA da yazılır — MDI sekme anahtarı odur.</summary>
    private async Task PersistBranchAsync()
    {
        try
        {
            await _uiSettings.SetWorkingBranchAsync(_currentBranchId?.ToString());
        }
        catch { /* ayar yazılamazsa sessiz — seçim oturumda geçerli kalır */ }
    }

    private async Task PersistVaultAsync()
    {
        try
        {
            await _uiSettings.SetWorkingVaultAsync(_currentVaultId?.ToString());
        }
        catch { /* ayar yazılamazsa sessiz — seçim oturumda geçerli kalır */ }
    }
}
