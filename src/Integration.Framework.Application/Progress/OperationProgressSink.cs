using System;
using Integration.Framework.Progress;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Application.Progress;

/// <summary><see cref="IOperationProgressSink"/>'in tek uygulaması — SCOPED: Blazor Server'da circuit başına bir
/// tane; app service (aynı scope) yazar, bileşen (aynı scope) dinler. HTTP API isteğinde de scoped'tur ama kimse
/// dinlemez — rapor kaybolur, zararsız.</summary>
public class OperationProgressSink : IOperationProgressSink, IScopedDependency
{
    public event Action<OperationProgress>? Reported;

    public OperationProgress? Current { get; private set; }

    public void Report(OperationProgress progress)
    {
        Current = progress;
        Reported?.Invoke(progress);
    }

    public void Complete()
    {
        Current = null;
        Reported?.Invoke(new OperationProgress(string.Empty, 0, 0));
    }
}
