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

    Task<ConfirmDialogResult> ConfirmDeleteAsync(string message, string? title = null);
    void CloseDialog(ConfirmDialogResult result);

    void ShowSuccessToast(string message);
    void ShowErrorToast(string message);
    void ShowWarningToast(string message);
}
