using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Services.Base;

public class DefaultPopupService : IPopupService, IScopedDependency
{
    public event Action? OnShow;
    public event Action? OnHide;

    public Type? ComponentType { get; private set; }
    public Dictionary<string, object>? ComponentParameters { get; private set; }
    public PopupOptions? Options { get; private set; }
    public bool IsVisible { get; private set; }
    public Func<Task<bool>>? CloseGuard { get; set; }

    private TaskCompletionSource<object?>? _tcs;

    public Task<object?> ShowAsync(Type componentType, Dictionary<string, object>? parameters = null, PopupOptions? options = null)
    {
        ComponentType = componentType;
        ComponentParameters = parameters ?? new Dictionary<string, object>();
        Options = options ?? new PopupOptions();
        CloseGuard = null; // yeni içerik kendi guard'ını set edene kadar temiz
        IsVisible = true;

        _tcs = new TaskCompletionSource<object?>();
        OnShow?.Invoke();

        return _tcs.Task;
    }

    public void Close(object? result = null)
    {
        IsVisible = false;
        ComponentType = null;
        ComponentParameters = null;
        Options = null;
        CloseGuard = null;

        OnHide?.Invoke();

        if (_tcs != null && !_tcs.Task.IsCompleted)
        {
            _tcs.SetResult(result);
            _tcs = null;
        }
    }
}
