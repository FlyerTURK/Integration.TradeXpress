using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Settings;

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

    bool IsLoaded { get; }

    event Action? Changed;

    Task EnsureLoadedAsync();
    Task SetBranchAsync(Guid? branchId);

    /// <summary>
    /// Şube/erişim kümesini sunucudan YENİDEN yükler (GetMyBranchesAsync). Grant runtime'da değişince
    /// (admin ScopedRoles'ü daralttı/genişletti) çağrılmalı: cache'li <see cref="Branches"/> ve türetilen
    /// <see cref="CurrentCompanyId"/> tazelenir; geçersizleşen seçim ilk izinli şubeye düşer. Tam
    /// invalidasyon-push yok — bu yeniden-yükleme kapısı yeterli (grant değişince tetiklenmeli).
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// Çalışma bağlamı (working context) — kullanıcının seçili çalışma ŞUBESİ (Company + Branch). Sol menü
/// footer'ındaki iki-kolonlu combo bunu sürer. Kalıcılık SUNUCU TARAFINDA, per-user (ABP Setting Management
/// → AbpSettings, <see cref="IUserUiSettingAppService"/>): cihazdan bağımsız, kullanıcıyla taşınır. İlk
/// yüklemede saklı seçim hâlâ geçerliyse o, değilse İLK şube otomatik seçilir → combo asla boş kalmaz.
/// Scoped (Blazor Server circuit = kullanıcı oturumu). İleride kapsam (scoped) yetki filtrelemesi bunu okuyacak.
/// </summary>
public class WorkingContextService : IWorkingContextService
{
    private readonly IBranchAppService _branchAppService;
    private readonly IUserUiSettingAppService _uiSettings;

    private List<BranchListDto> _branches = new();
    private IReadOnlyList<Guid> _allowedCompanyIds = Array.Empty<Guid>();
    private Guid? _currentBranchId;

    /// <summary>Paylaşılan yükleme Task'ı — eşzamanlı ikinci çağıran AYNI yüklemeyi bekler (boş şube listesi penceresi yok).</summary>
    private Task? _loadTask;

    public WorkingContextService(IBranchAppService branchAppService, IUserUiSettingAppService uiSettings)
    {
        _branchAppService = branchAppService;
        _uiSettings = uiSettings;
    }

    public IReadOnlyList<BranchListDto> Branches => _branches;
    public Guid? CurrentBranchId => _currentBranchId;
    public BranchListDto? CurrentBranch => _branches.FirstOrDefault(b => b.Id == _currentBranchId);
    public Guid? CurrentCompanyId => CurrentBranch?.CompanyId;
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
        _allowedCompanyIds = _branches.Select(b => b.CompanyId).Distinct().ToList();

        var stored = await TryReadStoredAsync();
        // Saklı seçim izinli kümede (server-filtreli _branches) hâlâ geçerliyse onu kullan; aksi halde
        // İLK İZİNLİ şube (combo boş kalmasın + yetkisiz saklı seçim izinli olana düşer).
        _currentBranchId = (stored is { } s && _branches.Any(b => b.Id == s))
            ? stored
            : _branches.FirstOrDefault()?.Id;

        if (_currentBranchId != stored)
            await PersistAsync();

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
        await PersistAsync();
        Changed?.Invoke();
    }

    public Task RefreshAsync()
    {
        // Paylaşılan Task'ı sıfırla → EnsureLoadedAsync yeniden yükler (hata durumu / eşzamanlılık aynı
        // yerden yönetilir). Grant değişimi sonrası çağrılınca _branches + AllowedCompanyIds tazelenir.
        _loadTask = null;
        return EnsureLoadedAsync();
    }

    private async Task<Guid?> TryReadStoredAsync()
    {
        try
        {
            var raw = await _uiSettings.GetWorkingBranchAsync();
            return Guid.TryParse(raw, out var g) ? g : null;
        }
        catch { return null; } // ayar okunamazsa sessiz → ilk şubeye düşer
    }

    private async Task PersistAsync()
    {
        try
        {
            await _uiSettings.SetWorkingBranchAsync(_currentBranchId?.ToString());
        }
        catch { /* ayar yazılamazsa sessiz — seçim oturumda geçerli kalır */ }
    }
}
