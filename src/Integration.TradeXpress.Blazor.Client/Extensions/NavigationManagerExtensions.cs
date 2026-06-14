using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Integration.TradeXpress.Blazor.Client.Extensions;

/// <summary>
/// <see cref="NavigationManager"/> için query-string yardımcıları.
/// </summary>
public static class NavigationManagerExtensions
{
    /// <summary>Geçerli URL'den verilen query parametresini döndürür; yoksa null.</summary>
    public static string? GetQueryParam(this NavigationManager navigationManager, string key)
    {
        var uri = navigationManager.ToAbsoluteUri(navigationManager.Uri);
        if (QueryHelpers.ParseQuery(uri.Query).TryGetValue(key, out var value))
        {
            return value.ToString();
        }
        return null;
    }
}
