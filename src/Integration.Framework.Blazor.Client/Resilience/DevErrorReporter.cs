using Integration.Framework.Blazor.Client.Services.Base;
using Volo.Abp.DependencyInjection;

namespace Integration.Framework.Blazor.Client.Resilience;

/// <summary>
/// <see cref="IClientErrorReporter"/> implementasyonu: yakalanan teknik .NET hatasını <see cref="DevErrorSink"/>'e
/// yazar → Developer Error Panel'de görünür. Blazor Server'da ILogger tarayıcı console'una gitmediğinden
/// (JS console-yakalama bu hataları görmez) caught exception'ları panele taşıyan tek yol budur.
/// </summary>
public sealed class DevErrorReporter : IClientErrorReporter, ITransientDependency
{
    private readonly DevErrorSink _sink;

    public DevErrorReporter(DevErrorSink sink)
    {
        _sink = sink;
    }

    public void Report(string message, string? detail = null)
    {
        _sink.Add(new DevErrorEntry
        {
            Source = "dotnet",
            Level = "exception",
            Message = message,
            Stack = detail,
        });
    }
}
