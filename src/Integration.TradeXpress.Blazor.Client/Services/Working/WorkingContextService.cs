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
    bool IsLoaded { get; }

    event Action? Changed;

    Task EnsureLoadedAsync();
    Task SetBranchAsync(Guid? branchId);
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
        var result = await _branchAppService.GetListAsync(new BranchListRequestDto { MaxResultCount = 1000 });
        _branches = result.Items
            .OrderBy(b => b.CompanyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stored = await TryReadStoredAsync();
        // Saklı seçim (server-side, per-user) hâlâ geçerliyse onu kullan; aksi halde İLK kayıt (combo boş kalmasın).
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
        _currentBranchId = branchId;
        await PersistAsync();
        Changed?.Invoke();
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
