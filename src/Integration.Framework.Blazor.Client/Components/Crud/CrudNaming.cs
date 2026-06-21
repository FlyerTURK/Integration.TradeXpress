using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>CRUD adlandırma yardımcıları — TGetDto adından lokalize entity başlığını üreten TEK kaynak.
/// Hem <c>CrudPageBase.EditTitle</c> hem <c>CrudEditComponentBase.EditFormCaption</c> bunu kullanır (DRY).</summary>
internal static class CrudNaming
{
    /// <summary>"<c>XGetDto</c>" → <c>L["X"]</c>. Entity tür başlığı (popup/tab/top-panel + liste açılış başlığı ortak).</summary>
    public static string EntityCaption(Type getDtoType, IStringLocalizer localizer)
    {
        var name = getDtoType.Name;
        if (name.EndsWith("GetDto", StringComparison.Ordinal))
            name = name[..^"GetDto".Length];
        return localizer[name];
    }
}
