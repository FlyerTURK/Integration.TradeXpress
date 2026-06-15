using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;

namespace Integration.TradeXpress.Settings;

[Authorize]
public class UserUiSettingAppService : TradeXpressAppService, IUserUiSettingAppService
{
    private readonly ISettingManager _settingManager;

    public UserUiSettingAppService(ISettingManager settingManager)
    {
        _settingManager = settingManager;
    }

    public async Task<string?> GetGridStateAsync(string gridKey)
    {
        var json = await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.GridStates);
        if (string.IsNullOrEmpty(json) || json == "{}") return null;

        try
        {
            var states = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (states != null && states.TryGetValue(gridKey, out var gridState))
            {
                return gridState;
            }
        }
        catch
        {
            // Json parse error
        }
        return null;
    }

    public async Task SetGridStateAsync(string gridKey, string stateJson)
    {
        var json = await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.GridStates);
        var states = new Dictionary<string, string>();
        
        if (!string.IsNullOrEmpty(json) && json != "{}")
        {
            try
            {
                states = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch { }
        }

        states[gridKey] = stateJson;
        var newJson = JsonSerializer.Serialize(states);
        
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.GridStates, newJson);
    }

    public async Task<string> GetMdiTabsAsync()
    {
        var json = await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.MdiTabs);
        return string.IsNullOrEmpty(json) ? "[]" : json;
    }

    public async Task SetMdiTabsAsync(string stateJson)
    {
        if (string.IsNullOrEmpty(stateJson)) stateJson = "[]";
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.MdiTabs, stateJson);
    }

    public async Task<string?> GetThemeAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.Theme);
    }

    public async Task SetThemeAsync(string stateJson)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.Theme, stateJson ?? "");
    }
}
