using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Extensions;

/// <summary>
/// <see cref="ICurrentUser"/> için UI yardımcıları.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>Avatar için kullanıcının baş harfini döndürür; ad yoksa "?".</summary>
    public static string GetInitial(this ICurrentUser currentUser)
        => string.IsNullOrEmpty(currentUser.Name)
            ? "?"
            : currentUser.Name![0].ToString().ToUpperInvariant();
}
