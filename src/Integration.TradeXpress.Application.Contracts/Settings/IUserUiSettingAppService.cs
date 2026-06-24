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

    /// <summary>Seçili çalışma şubesi (Branch.Id) — per-user, cihazdan bağımsız.</summary>
    Task<string?> GetWorkingBranchAsync();
    Task SetWorkingBranchAsync(string? branchId);
}
