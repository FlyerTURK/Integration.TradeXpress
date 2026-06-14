using System.Collections.Generic;
using System.Globalization;

namespace Integration.TradeXpress.Blazor.Client.Globalization;

/// <summary>
/// Uygulamanın desteklediği UI kültürleri. Dil seçim combobox'ı bunları listeler;
/// gösterilen metin <see cref="CultureInfo.NativeName"/>, değer <see cref="CultureInfo.Name"/>.
/// </summary>
public static class CultureCatalog
{
    /// <summary>ASP.NET kültür cookie adı (sunucu/API tarafı request-localization için).</summary>
    public const string CookieName = ".AspNetCore.Culture";

    /// <summary>WASM istemci UI kültürünün kalıcı kaynağı (Program.Main açılışta okur).</summary>
    public const string StorageKey = "tx.culture";

    public static readonly IReadOnlyList<CultureInfo> Supported = new[]
    {
        new CultureInfo("tr"),
        new CultureInfo("en"),
    };
}
