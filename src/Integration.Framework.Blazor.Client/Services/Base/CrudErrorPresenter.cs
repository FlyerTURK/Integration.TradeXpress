using System.Collections;
using Integration.Framework.Blazor.Client.Components.Crud;
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
    /// (<c>MapCodeNamespace</c>) bulunan kaynakla lokalize eder + <c>{Placeholder}</c>'ları exception
    /// <c>Data</c>'sıyla doldurur (ABP HTTP pipeline'ının yaptığını in-process yapar — aksi halde toast'ta
    /// ham "{Property} zorunludur." görünür). Eşleşme/çeviri yoksa null.</summary>
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

            var factory = serviceProvider.GetRequiredService<IStringLocalizerFactory>();
            var localized = factory.Create(resourceType)[code];
            if (!localized.ResourceNotFound)
            {
                return ApplyDataPlaceholders(localized.Value, current, factory);
            }
        }

        return null;
    }

    /// <summary>Mesajdaki <c>{Key}</c> placeholder'larını exception <c>Data</c> girdileriyle doldurur. Değer
    /// (genelde property adı) app default resource'undan çevrilmeye çalışılır ("DisplayName:X" → "X" → ham ad).</summary>
    private static string ApplyDataPlaceholders(string message, Exception ex, IStringLocalizerFactory factory)
    {
        if (ex.Data.Count == 0 || !message.Contains('{'))
        {
            return message;
        }

        var appLocalizer = CrudComponentBase.DefaultLocalizationResource is { } resource
            ? factory.Create(resource)
            : null;

        foreach (DictionaryEntry entry in ex.Data)
        {
            if (entry.Key is not string key)
            {
                continue;
            }

            var raw = entry.Value?.ToString() ?? string.Empty;
            message = message.Replace("{" + key + "}", LocalizeValue(raw, appLocalizer));
        }

        return message;
    }

    /// <summary>Placeholder DEĞERİNİ (property adı) insan-okunur çevirir: "DisplayName:X" anahtarı → çıplak "X"
    /// anahtarı → çeviri yoksa ham ad (fail-open; en kötü ihtimal İngilizce identifier görünür, boş placeholder değil).</summary>
    private static string LocalizeValue(string raw, IStringLocalizer? localizer)
    {
        if (localizer is null || raw.Length == 0)
        {
            return raw;
        }

        var display = localizer["DisplayName:" + raw];
        if (!display.ResourceNotFound)
        {
            return display.Value;
        }

        var plain = localizer[raw];
        return plain.ResourceNotFound ? raw : plain.Value;
    }
}
