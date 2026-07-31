using System.Reflection;
using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Alan (property) adının kullanıcıya görünen hâlini çözen TEK merkez.
///
/// <para><b>Neden tek merkez (2026-08-01 denetimi):</b> aynı çözüm üç ayrı yerde üç ayrı biçimde yaşıyordu
/// (client validator yalnız bare anahtar, server hata sunucusu "DisplayName:X → X", eski yığın kendi kompozisyonu)
/// ve dört ayrı anahtar-uzayı doğmuştu — her yeni DTO'da hangisinin geçerli olduğu insan hafızasına kalıyordu.
/// Zincir artık HER tüketicide aynı: <c>[Display(Name)]</c> → <c>DisplayName:X</c> → <c>X</c> → ham ad.</para>
///
/// <para><b>Anahtar konvansiyonu:</b> form caption'ları alan adlarını zaten bare anahtarla çevirir
/// (<c>L["Code"]</c>="Kod") — validation da aynı sözlüğü kullanır; alan-özel sapma gerekirse
/// <c>DisplayName:X</c> anahtarı bare'i ezer. Fail-open: hiçbir anahtar yoksa ham ad görünür
/// (boş placeholder asla üretilmez).</para>
/// </summary>
public static class FieldNameLocalizer
{
    /// <summary>Property bilgisi ELDEyken çözer — <c>[Display(Name)]</c> önceliği yalnız bu aşırı yüklemede
    /// devreye girebilir (attribute bilgisi string'e indirgenmeden).</summary>
    public static string Resolve(IStringLocalizer? localizer, PropertyInfo property)
    {
        var displayKey = property.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>()?.Name;
        if (!string.IsNullOrEmpty(displayKey) && localizer is not null)
        {
            return localizer[displayKey!].Value;
        }

        return Resolve(localizer, property.Name);
    }

    /// <summary>Yalnız ham ad ELDEyken çözer (server hata placeholder'ları gibi) —
    /// <c>DisplayName:X</c> → <c>X</c> → ham ad.</summary>
    public static string Resolve(IStringLocalizer? localizer, string rawName)
    {
        if (localizer is null || rawName.Length == 0)
        {
            return rawName;
        }

        var display = localizer["DisplayName:" + rawName];
        if (!display.ResourceNotFound)
        {
            return display.Value;
        }

        var plain = localizer[rawName];
        return plain.ResourceNotFound ? rawName : plain.Value;
    }
}
