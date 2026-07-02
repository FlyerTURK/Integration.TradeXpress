using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.SettingManagement;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Settings;

[Authorize]
public class UserUiSettingAppService : TradeXpressAppService, IUserUiSettingAppService
{
    private readonly ISettingManager _settingManager;
    private readonly IRepository<UserGridLayout, Guid> _gridLayoutRepository;

    public UserUiSettingAppService(
        ISettingManager settingManager,
        IRepository<UserGridLayout, Guid> gridLayoutRepository)
    {
        _settingManager = settingManager;
        _gridLayoutRepository = gridLayoutRepository;
    }

    // Grid düzenleri AYRI TABLODA (AppUserGridLayouts) per-grid satır olarak tutulur; Layout = nvarchar(max).
    // (Önce tek AbpSettings.Value'da JSON sözlüğüydü ama büyüyünce TRUNCATE oluyordu → ayrı tablo.)

    public async Task ResetGridStatesAsync()
    {
        var userId = CurrentUser.GetId();
        await _gridLayoutRepository.DeleteAsync(x => x.UserId == userId, autoSave: true);
    }

    public async Task<string?> GetGridStateAsync(string gridKey)
    {
        var userId = CurrentUser.GetId();
        var entity = await _gridLayoutRepository.FirstOrDefaultAsync(
            x => x.UserId == userId && x.GridKey == gridKey);
        return string.IsNullOrEmpty(entity?.Layout) ? null : entity!.Layout;
    }

    public async Task SetGridStateAsync(string gridKey, string stateJson)
    {
        try
        {
            var userId = CurrentUser.GetId();
            var entity = await _gridLayoutRepository.FirstOrDefaultAsync(
                x => x.UserId == userId && x.GridKey == gridKey);

            if (entity == null)
            {
                await _gridLayoutRepository.InsertAsync(
                    new UserGridLayout(userId, gridKey, stateJson), autoSave: true);
            }
            else
            {
                entity.SetLayout(stateJson);
                await _gridLayoutRepository.UpdateAsync(entity, autoSave: true);
            }
        }
        catch (Exception ex)
        {
            // Hata sessizce log'lansın — grid state kaybolsa bile app çalışmaya devam etsin
            Logger.LogWarning(ex, $"Failed to save grid state for {gridKey}: {ex.Message}");
        }
    }

    public async Task<string> GetMdiTabsAsync()
    {
        var branchId = await GetWorkingBranchAsync();
        var key = string.IsNullOrEmpty(branchId) ? TradeXpressUiSettingNames.MdiTabs : $"{TradeXpressUiSettingNames.MdiTabs}_{branchId}";
        var json = await GetGridStateAsync(key);
        return string.IsNullOrEmpty(json) ? "[]" : json;
    }

    public async Task SetMdiTabsAsync(string stateJson)
    {
        var branchId = await GetWorkingBranchAsync();
        var key = string.IsNullOrEmpty(branchId) ? TradeXpressUiSettingNames.MdiTabs : $"{TradeXpressUiSettingNames.MdiTabs}_{branchId}";
        if (string.IsNullOrEmpty(stateJson)) stateJson = "[]";
        await SetGridStateAsync(key, stateJson);
    }

    public async Task<string?> GetThemeAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.Theme);
    }

    public async Task SetThemeAsync(string stateJson)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.Theme, stateJson ?? "");
    }

    public async Task<string?> GetWorkingBranchAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.WorkingBranch);
    }

    public async Task SetWorkingBranchAsync(string? branchId)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.WorkingBranch, branchId ?? "");
    }
}
