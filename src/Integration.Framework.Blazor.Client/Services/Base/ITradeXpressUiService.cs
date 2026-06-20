using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Services.Base;

public enum ConfirmDialogResult
{
    Yes,
    No,
    Cancel
}

public interface ITradeXpressUiService : IScopedDependency
{
    event Action? OnDialogChanged;
    bool IsDialogVisible { get; }
    string? DialogMessage { get; }
    string? DialogTitle { get; }
    /// <summary>Onay (Yes) butonu metni — null ise varsayılan "Evet".</summary>
    string? DialogYesText { get; }
    /// <summary>Ret (No) butonu metni — null ise varsayılan "Hayır".</summary>
    string? DialogNoText { get; }
    /// <summary>Cancel butonu gösterilsin mi? false ise iptal görevini yalnız pencerenin çarpısı yapar.</summary>
    bool DialogShowCancel { get; }
    /// <summary>Varsayılan (primary + odaklı) buton Yes mi? true → Yes, false → No. Silme'de güvenli olan No, dirty'de Kaydet (Yes).</summary>
    bool DialogDefaultYes { get; }

    Task<ConfirmDialogResult> ConfirmDeleteAsync(string message, string? title = null);
    /// <summary>Genel onay diyaloğu — özel buton metinleri, Cancel gizleme ve varsayılan buton seçimiyle.</summary>
    Task<ConfirmDialogResult> ConfirmAsync(string message, string? title, string? yesText, string? noText, bool showCancel, bool defaultYes = false);
    void CloseDialog(ConfirmDialogResult result);

    void ShowSuccessToast(string message);
    void ShowErrorToast(string message);
    void ShowWarningToast(string message);
}
