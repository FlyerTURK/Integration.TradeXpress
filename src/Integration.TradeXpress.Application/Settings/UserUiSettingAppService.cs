using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Application.Services;

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
        // Her grid kendi ayarında depolansın (gridKey başına bir setting anahtarı)
        var settingKey = $"{TradeXpressUiSettingNames.GridStates}_{gridKey}";
        var json = await SettingProvider.GetOrNullAsync(settingKey);
        return string.IsNullOrEmpty(json) ? null : json;
    }

    public async Task SetGridStateAsync(string gridKey, string stateJson)
    {
        // Her grid kendi ayarında depolansın — truncate sorunu ortadan kalkar
        var settingKey = $"{TradeXpressUiSettingNames.GridStates}_{gridKey}";
        try
        {
            await _settingManager.SetForCurrentUserAsync(settingKey, stateJson);
        }
        catch (Exception ex)
        {
            // Hata sessizce log'lansın — grid state kaybolsa bile app çalışmaya devam etsin
            Logger.LogWarning(ex, $"Failed to save grid state for {gridKey}: {ex.Message}");
        }
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
