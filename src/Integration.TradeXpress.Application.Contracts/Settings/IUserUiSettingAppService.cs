using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Settings;

public interface IUserUiSettingAppService : IApplicationService
{
    Task<string?> GetGridStateAsync(string gridKey);
    Task SetGridStateAsync(string gridKey, string stateJson);

    /// <summary>Mevcut kullanıcının TÜM grid kolon-düzeni ayarlarını siler (stil/düzen sıfırla).</summary>
    Task ResetGridStatesAsync();

    Task<string> GetMdiTabsAsync();
    Task SetMdiTabsAsync(string stateJson);

    Task<string?> GetThemeAsync();
    Task SetThemeAsync(string stateJson);

    /// <summary>Boyut modu (Small/Medium/Large) — per-user, cihazdan bağımsız.</summary>
    Task<string?> GetSizeModeAsync();
    Task SetSizeModeAsync(string? sizeMode);

    /// <summary>UI dili (tr/en) — per-user, cihazdan bağımsız; cookie yalnız anonim aynadır.</summary>
    Task<string?> GetCultureAsync();
    Task SetCultureAsync(string? culture);

    /// <summary>Seçili çalışma şubesi (Branch.Id) — per-user, cihazdan bağımsız.</summary>
    Task<string?> GetWorkingBranchAsync();
    Task SetWorkingBranchAsync(string? branchId);

    /// <summary>Seçili çalışma kasası (Vault.Id) — per-user, cihazdan bağımsız.</summary>
    Task<string?> GetWorkingVaultAsync();
    Task SetWorkingVaultAsync(string? vaultId);
}
