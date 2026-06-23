using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.SettingManagement;

namespace Integration.TradeXpress.Settings;

[Authorize]
public class UserUiSettingAppService : TradeXpressAppService, IUserUiSettingAppService
{
    private readonly ISettingManager _settingManager;

    public UserUiSettingAppService(ISettingManager settingManager)
    {
        _settingManager = settingManager;
    }

    // Grid düzenleri TEK tanımlı ayarda ("TradeXpress.UI.GridStates", default "{}") JSON SÖZLÜĞÜ olarak tutulur:
    // { gridKey → layoutJson }. (Önceki "GridStates_<grid>" per-grid adı ABP'de TANIMSIZ → "Undefined setting"
    // ile sessizce kaydedilemiyordu; tanım yalnız taban adı içeriyor.)

    /// <summary>Mevcut GridStates sözlüğünü oku (bozuksa boş döner).</summary>
    private async Task<Dictionary<string, string>> ReadGridStatesAsync()
    {
        var json = await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.GridStates);
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(); }
        catch { return new(); }
    }

    public async Task ResetGridStatesAsync()
    {
        // Tüm grid düzenlerini sıfırla → tanımlı ayarı boş sözlüğe ("{}") al.
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.GridStates, "{}");
    }

    public async Task<string?> GetGridStateAsync(string gridKey)
    {
        var dict = await ReadGridStatesAsync();
        return dict.TryGetValue(gridKey, out var v) && !string.IsNullOrEmpty(v) ? v : null;
    }

    public async Task SetGridStateAsync(string gridKey, string stateJson)
    {
        try
        {
            var dict = await ReadGridStatesAsync();
            dict[gridKey] = stateJson;
            await _settingManager.SetForCurrentUserAsync(
                TradeXpressUiSettingNames.GridStates, JsonSerializer.Serialize(dict));
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
