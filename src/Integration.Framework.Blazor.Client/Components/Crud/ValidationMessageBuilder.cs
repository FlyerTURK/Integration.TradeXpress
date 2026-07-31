using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Bir <see cref="ValidationAttribute"/> ihlalinin KULLANICI mesajını üreten tek merkez —
/// <c>Validation:*</c> anahtar ailesi burada tüketilir.
///
/// <para><b>Neden ayrı sınıf:</b> aynı switch daha önce yalnız <c>LocalizedDataAnnotationsValidator</c>'ın
/// içindeydi; graf doğrulayıcısı (<see cref="GraphValidator"/>) gelince ikinci kopya doğacaktı. Mesaj
/// şablonu tek yerde yaşar — yeni attribute türü desteklenecekse yalnız buraya eklenir.</para>
/// </summary>
public static class ValidationMessageBuilder
{
    /// <summary>İhlal mesajı — <paramref name="displayName"/> çözülmüş (lokalize) alan adıdır
    /// (<see cref="FieldNameLocalizer"/>). Bilinmeyen attribute türünde attribute'un kendi mesajına düşülür.</summary>
    public static string Build(IStringLocalizer localizer, ValidationAttribute attribute, string displayName)
    {
        return attribute switch
        {
            RequiredAttribute => localizer["Validation:Required", displayName].Value,
            StringLengthAttribute s when s.MinimumLength > 0 => localizer["Validation:LengthRange", displayName, s.MinimumLength, s.MaximumLength].Value,
            StringLengthAttribute s => localizer["Validation:MaxLength", displayName, s.MaximumLength].Value,
            MaxLengthAttribute m => localizer["Validation:MaxLength", displayName, m.Length].Value,
            MinLengthAttribute m => localizer["Validation:MinLength", displayName, m.Length].Value,
            EmailAddressAttribute => localizer["Validation:Email", displayName].Value,
            RangeAttribute r => localizer["Validation:Range", displayName, r.Minimum, r.Maximum].Value,
            _ => attribute.FormatErrorMessage(displayName),
        };
    }
}
