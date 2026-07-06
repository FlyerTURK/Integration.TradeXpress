using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Localization.ExceptionHandling;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Exception → kullanıcı-dostu, LOKALİZE mesaj (yeniden-kullanılabilir). Blazor Server'da app service'ler
/// IN-PROCESS çağrıldığından ABP'nin HTTP pipeline exception-lokalizasyonu çalışmaz → <see cref="BusinessException"/>
/// yalnız <c>Code</c> taşır, <c>Message</c> ya ham kod ya da ".NET generic" metnidir. Bu yardımcı önce kodu
/// <c>MapCodeNamespace</c> eşlemesindeki kaynakla çevirir; bulamazsa <see cref="CrudErrorFormatter"/> fallback'ine düşer.
/// Sıra kritiktir: LocalizeErrorCode ÖNCE (aksi halde formatter ham kodu/generic metni döndürebilir).
/// </summary>
public static class CrudErrorPresenter
{
    /// <summary>Lokalize dostu mesaj; kural eşleşmezse null (çağıran genel mesaja düşer, teknik detayı loglar).</summary>
    public static string? ToFriendlyMessage(Exception ex, IServiceProvider serviceProvider)
    {
        return LocalizeErrorCode(ex, serviceProvider) ?? CrudErrorFormatter.Extract(ex);
    }

    /// <summary>Hata kodlu (<see cref="BusinessException"/>) exception'ın kodunu, kod-namespace eşlemesinden
    /// (<c>MapCodeNamespace</c>) bulunan kaynakla lokalize eder. Eşleşme/çeviri yoksa null.</summary>
    public static string? LocalizeErrorCode(Exception ex, IServiceProvider serviceProvider)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is not BusinessException { Code: { } code } || !code.Contains(':'))
            {
                continue;
            }

            var ns = code.Substring(0, code.IndexOf(':'));
            var mappings = serviceProvider
                .GetService<IOptions<AbpExceptionLocalizationOptions>>()
                ?.Value.ErrorCodeNamespaceMappings;
            if (mappings is null || !mappings.TryGetValue(ns, out var resourceType))
            {
                continue;
            }

            var localized = serviceProvider
                .GetRequiredService<IStringLocalizerFactory>()
                .Create(resourceType)[code];
            if (!localized.ResourceNotFound)
            {
                return localized.Value;
            }
        }

        return null;
    }
}
