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

    /// <summary>Edit sayfası, model yüklenince/dirty değişince sekmesinin yapısal başlığını günceller
    /// (3-satır caption + dirty "*"). Bilinmeyen id → no-op.</summary>
    void UpdateTabHeader(System.Guid tabId, TabHeaderData header);

    /// <summary>SplitView'da embedded edit, başlığı EZMEDEN sadece dirty bayrağını set eder (liste tab'ına "*").
    /// Bilinmeyen id → no-op.</summary>
    void SetTabDirty(System.Guid tabId, bool isDirty);
}
