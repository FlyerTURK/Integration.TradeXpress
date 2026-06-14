using System;
using System.Threading.Tasks;
using DevExpress.Blazor;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Services.Base;

public class TradeXpressUiService : ITradeXpressUiService, IScopedDependency
{
    private readonly IToastNotificationService _toastNotificationService;

    public event Action? OnDialogChanged;
    public bool IsDialogVisible { get; private set; }
    public string? DialogMessage { get; private set; }
    public string? DialogTitle { get; private set; }

    private TaskCompletionSource<ConfirmDialogResult>? _dialogTcs;

    public TradeXpressUiService(IToastNotificationService toastNotificationService)
    {
        _toastNotificationService = toastNotificationService;
    }

    public Task<ConfirmDialogResult> ConfirmDeleteAsync(string message, string? title = null)
    {
        // Cancel previous if any
        _dialogTcs?.TrySetResult(ConfirmDialogResult.Cancel);

        _dialogTcs = new TaskCompletionSource<ConfirmDialogResult>();
        DialogMessage = message;
        DialogTitle = title ?? "Onay";
        IsDialogVisible = true;
        
        OnDialogChanged?.Invoke();
        
        return _dialogTcs.Task;
    }

    public void CloseDialog(ConfirmDialogResult result)
    {
        IsDialogVisible = false;
        _dialogTcs?.TrySetResult(result);
        OnDialogChanged?.Invoke();
    }

    public void ShowSuccessToast(string message)
    {
        _toastNotificationService.ShowToast(new ToastOptions
        {
            Text = message,
            RenderStyle = ToastRenderStyle.Success
        });
    }

    public void ShowErrorToast(string message)
    {
        _toastNotificationService.ShowToast(new ToastOptions
        {
            Text = message,
            RenderStyle = ToastRenderStyle.Danger
        });
    }

    public void ShowWarningToast(string message)
    {
        _toastNotificationService.ShowToast(new ToastOptions
        {
            Text = message,
            RenderStyle = ToastRenderStyle.Warning
        });
    }
}
