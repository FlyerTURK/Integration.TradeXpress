using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// MDI sekme açma soyutlaması (framework seviyesi) — uygulamadaki TabManager bunu uygular.
/// CrudLayout, edit formunu sekmede açma (Tab modu) için bunu enjekte eder; böylece framework
/// uygulamanın somut TabManager tipine bağımlı olmaz.
/// </summary>
public interface IMdiTabOpener
{
    /// <summary>Aynı URL'li sekme varsa aktive eder, yoksa yeni iç sekme açar.</summary>
    Task OpenOrActivateAsync(string url, string title, string? icon = null);
}
