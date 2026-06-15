using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Settings;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Services;

public class TradeXpressUiStateService : IUiStateService, ITransientDependency
{
    private readonly IUserUiSettingAppService _userUiSettingAppService;

    public TradeXpressUiStateService(IUserUiSettingAppService userUiSettingAppService)
    {
        _userUiSettingAppService = userUiSettingAppService;
    }

    public Task SaveGridStateAsync(string gridKey, string stateJson)
    {
        return _userUiSettingAppService.SetGridStateAsync(gridKey, stateJson);
    }

    public Task<string?> GetGridStateAsync(string gridKey)
    {
        return _userUiSettingAppService.GetGridStateAsync(gridKey);
    }
}
