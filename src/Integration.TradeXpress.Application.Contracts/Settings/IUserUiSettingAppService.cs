using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Settings;

public interface IUserUiSettingAppService : IApplicationService
{
    Task<string?> GetGridStateAsync(string gridKey);
    Task SetGridStateAsync(string gridKey, string stateJson);
    
    Task<string> GetMdiTabsAsync();
    Task SetMdiTabsAsync(string stateJson);

    Task<string?> GetThemeAsync();
    Task SetThemeAsync(string stateJson);
}
