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

    // MdiTabs (+legacy "MdiTabs" öneki) AYNI TABLODA yaşadığından filtresiz silme sekmeleri de yok ederdi —
    // Ayarlar panelindeki ayrı "Sekmeleri sıfırla" butonuyla çelişirdi (kullanıcı yalnız grid düzenini
    // sıfırlamak isterken sekmeleri de kaybederdi). Sekme anahtarları hariç tutulur.
    public async Task ResetGridStatesAsync()
    {
        var userId = CurrentUser.GetId();
        await _gridLayoutRepository.DeleteAsync(
            x => x.UserId == userId
                && !x.GridKey.StartsWith(TradeXpressUiSettingNames.MdiTabs)
                && !x.GridKey.StartsWith(LegacyMdiTabsKeyPrefix),   // "MdiTabs" — bkz. BuildLegacyMdiTabsKey
            autoSave: true);
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
            await WriteGridStateAsync(gridKey, stateJson);
        }
        catch (Exception ex)
        {
            // Hata sessizce log'lansın — grid state kaybolsa bile app çalışmaya devam etsin
            Logger.LogWarning(ex, $"Failed to save grid state for {gridKey}: {ex.Message}");
        }
    }

    private async Task WriteGridStateAsync(string gridKey, string stateJson)
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

    // Anahtar SUNUCUDA çözülür: çalışma şubesi ABP setting'ten okunur (tek doğruluk kaynağı) — client'ın
    // working-context yükleme sırasına bağımlılık yok (eski client-side anahtar kurgusu ilk rehydrate'te
    // yanlış kovayı okuyabiliyordu).
    public async Task<string> GetMdiTabsAsync()
    {
        var branchId = await GetWorkingBranchAsync();
        var json = await GetGridStateAsync(BuildMdiTabsKey(branchId));
        if (string.IsNullOrEmpty(json))
        {
            // Anahtar göçü: eski TabManager ham "MdiTabs"/"MdiTabs_{branch}" anahtarına yazıyordu —
            // yeni anahtar boşsa mevcut kullanıcı verisi oradan devralınır.
            json = await GetGridStateAsync(BuildLegacyMdiTabsKey(branchId));
        }
        return string.IsNullOrEmpty(json) ? "[]" : json;
    }

    public async Task SetMdiTabsAsync(string stateJson)
    {
        var branchId = await GetWorkingBranchAsync();
        if (string.IsNullOrEmpty(stateJson)) stateJson = "[]";

        // Birincil yazım BİLİNÇLİ olarak SetGridStateAsync'in sessiz-yutma davranışını PAYLAŞMAZ: hata
        // TabManager.PersistAsync'e kadar yayılıp PersistFailed → kullanıcıya uyarı toast'ı üretmeli (Faz 1
        // "hata görünürlüğü" hedefi — SetGridStateAsync'in catch-all'ı bu yolu ölü bırakıyordu). Grid kolon
        // düzeni gibi ikincil/kozmetik veriler için sessiz-geçme felsefesi (SetGridStateAsync) burada
        // DEĞİŞTİRİLMİYOR — yalnız MDI sekmeleri için ayrı, yutmayan bir yazım yolu kullanılıyor.
        await WriteGridStateAsync(BuildMdiTabsKey(branchId), stateJson);

        // Çift-yazım köprüsü: rollback'te eski client legacy anahtardan okumaya devam edebilsin. İkincil/
        // best-effort — birincil yazım zaten başarılıysa bunun hatası kullanıcıya veri kaybı olarak yansımaz.
        // Göç tamamlanınca (bir sonraki sürüm) bu satır ve legacy anahtar kalkar.
        await SetGridStateAsync(BuildLegacyMdiTabsKey(branchId), stateJson);
    }

    private static string BuildMdiTabsKey(string? branchId)
        => string.IsNullOrEmpty(branchId) ? TradeXpressUiSettingNames.MdiTabs : $"{TradeXpressUiSettingNames.MdiTabs}_{branchId}";

    /// <summary>Eski TabManager'ın client-side kurduğu ham anahtar öneki — göç köprüsü + reset-filtresi için.</summary>
    private const string LegacyMdiTabsKeyPrefix = "MdiTabs";

    private static string BuildLegacyMdiTabsKey(string? branchId)
        => string.IsNullOrEmpty(branchId) ? LegacyMdiTabsKeyPrefix : $"{LegacyMdiTabsKeyPrefix}_{branchId}";

    public async Task<string?> GetThemeAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.Theme);
    }

    public async Task SetThemeAsync(string stateJson)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.Theme, stateJson ?? "");
    }

    public async Task<string?> GetSizeModeAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.SizeMode);
    }

    public async Task SetSizeModeAsync(string? sizeMode)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.SizeMode, sizeMode ?? "");
    }

    public async Task<string?> GetCultureAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.Culture);
    }

    public async Task SetCultureAsync(string? culture)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.Culture, culture ?? "");
    }

    public async Task<string?> GetWorkingBranchAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.WorkingBranch);
    }

    public async Task SetWorkingBranchAsync(string? branchId)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.WorkingBranch, branchId ?? "");
    }

    public async Task<string?> GetWorkingVaultAsync()
    {
        return await SettingProvider.GetOrNullAsync(TradeXpressUiSettingNames.WorkingVault);
    }

    public async Task SetWorkingVaultAsync(string? vaultId)
    {
        await _settingManager.SetForCurrentUserAsync(TradeXpressUiSettingNames.WorkingVault, vaultId ?? "");
    }
}
