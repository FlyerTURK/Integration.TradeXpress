using DevExpress.Blazor;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Services.Base;

public class UiInteractionService : IUiInteractionService, IScopedDependency
{
    private readonly IToastNotificationService _toastNotificationService;

    public event Action? OnDialogChanged;
    public bool IsDialogVisible { get; private set; }
    public string? DialogMessage { get; private set; }
    public string? DialogTitle { get; private set; }
    public string? DialogYesText { get; private set; }
    public string? DialogNoText { get; private set; }
    public bool DialogShowCancel { get; private set; } = true;
    public bool DialogShowNo { get; private set; } = true;
    public bool DialogDefaultYes { get; private set; }
    public bool DialogShowInput { get; private set; }
    public string? DialogInputLabel { get; private set; }
    public string? DialogInputValue { get; set; }
    public bool DialogInputRequired { get; private set; }

    private TaskCompletionSource<ConfirmDialogResult>? _dialogTcs;

    public UiInteractionService(IToastNotificationService toastNotificationService)
    {
        _toastNotificationService = toastNotificationService;
    }

    public Task<ConfirmDialogResult> ConfirmDeleteAsync(string message, string? title = null, string? yesText = null)
    {
        // Silme onayı iki butonlu: "{Kaydı/Kayıtları} Sil" + "Vazgeç" (No YOK) — güvenli varsayılan Vazgeç.
        return ConfirmAsync(message, title, yesText, noText: null, showCancel: true, defaultYes: false, showNo: false);
    }

    public Task<ConfirmDialogResult> ConfirmAsync(string message, string? title, string? yesText, string? noText, bool showCancel, bool defaultYes = false, bool showNo = true)
    {
        // Cancel previous if any
        _dialogTcs?.TrySetResult(ConfirmDialogResult.Cancel);

        _dialogTcs = new TaskCompletionSource<ConfirmDialogResult>();
        DialogMessage = message;
        DialogTitle = title ?? "Onay";
        DialogYesText = yesText;
        DialogNoText = noText;
        DialogShowCancel = showCancel;
        DialogShowNo = showNo;
        DialogDefaultYes = defaultYes;
        DialogShowInput = false;   // düz onay — girdi alanı YOK (PromptAsync'ten miras kalmış stale true olmasın)
        IsDialogVisible = true;

        OnDialogChanged?.Invoke();

        return _dialogTcs.Task;
    }

    public async Task<(ConfirmDialogResult Result, string? Text)> PromptAsync(
        string message, string? title, string inputLabel, string yesText, string? noText,
        bool showCancel, bool inputRequired = true, string? initialValue = null, bool showNo = true)
    {
        _dialogTcs?.TrySetResult(ConfirmDialogResult.Cancel);

        _dialogTcs = new TaskCompletionSource<ConfirmDialogResult>();
        DialogMessage = message;
        DialogTitle = title ?? "Onay";
        DialogYesText = yesText;
        DialogNoText = noText;
        DialogShowCancel = showCancel;
        DialogShowNo = showNo;
        DialogDefaultYes = false;
        DialogShowInput = true;
        DialogInputLabel = inputLabel;
        DialogInputValue = initialValue;
        DialogInputRequired = inputRequired;
        IsDialogVisible = true;

        OnDialogChanged?.Invoke();

        var result = await _dialogTcs.Task;
        var text = result == ConfirmDialogResult.Yes ? DialogInputValue : null;
        DialogShowInput = false;   // kapandı — sonraki düz ConfirmAsync çağrısına sızmasın
        return (result, text);
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
