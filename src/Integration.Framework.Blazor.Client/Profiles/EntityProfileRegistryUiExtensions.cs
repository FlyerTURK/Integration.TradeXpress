using Microsoft.Extensions.Localization;

namespace Integration.Framework.Blazor.Client.Profiles;

/// <summary>
/// EntityProfile'dan UI yüzey kimliğini (ikon + çoğul başlık) TEK KAYNAK çözen yardımcılar. Drill
/// sekmelerinde elle <c>TradeXpressIcons.X</c> + <c>L["Menu:X"]</c> yazmak yerine profilden türetir:
/// <code>&lt;DxTabPage Text="@Profiles.PluralTitle(\"Vault\", L)" TabIconCssClass="@Profiles.Icon(\"Vault\")"&gt;</code>
/// Bilinmeyen key → <see cref="IEntityProfileRegistry.GetByKey"/> fail-fast (sessiz değil; geliştirici hemen görür).
/// </summary>
public static class EntityProfileRegistryUiExtensions
{
    /// <summary>Profilin ikon CSS sınıfı (ham; lokalize EDİLMEZ) — DxTabPage.TabIconCssClass / ikon yüzeyleri için.</summary>
    public static string Icon(this IEntityProfileRegistry registry, string profileKey)
        => registry.GetByKey(profileKey).IconCssClass;

    /// <summary>Profilin çoğul başlığı, LOKALİZE — DxTabPage.Text / liste başlığı için.
    /// (<see cref="EntityProfile.PluralCaptionKey"/> ham bir kaynak anahtarıdır; mutlaka localizer'dan geçer.)</summary>
    public static string PluralTitle(this IEntityProfileRegistry registry, string profileKey, IStringLocalizer localizer)
        => localizer[registry.GetByKey(profileKey).PluralCaptionKey];
}
