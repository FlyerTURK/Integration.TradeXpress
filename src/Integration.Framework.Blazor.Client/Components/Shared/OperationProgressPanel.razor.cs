using System;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Progress;
using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>Uzun işlem ilerleme paneli — <see cref="IOperationProgressSink"/> abonesi. Markup'ta gerekçe.</summary>
public partial class OperationProgressPanel : CrudComponentBase, IDisposable
{
    [Inject] private IOperationProgressSink ProgressSink { get; set; } = default!;

    private OperationProgress? Current { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Current = ProgressSink.Current;
        ProgressSink.Reported += OnReported;
    }

    // Etiket: "Faz · 12 / 224" (toplam biliniyorsa) ya da "Faz · 150" (akan sayaç).
    private static string LabelOf(OperationProgress p)
    {
        if (p.Total is > 0)
        {
            return $"{p.Phase} · {p.Current} / {p.Total}";
        }

        return p.Current > 0 ? $"{p.Phase} · {p.Current}" : p.Phase;
    }

    private void OnReported(OperationProgress progress)
    {
        Current = string.IsNullOrEmpty(progress.Phase) ? null : progress;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        ProgressSink.Reported -= OnReported;
    }
}
