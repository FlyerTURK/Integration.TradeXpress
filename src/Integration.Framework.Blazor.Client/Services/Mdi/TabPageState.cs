using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// Sekme sayfa-içi durum (PageState) okuma/yazma yardımcısı — serileştirme, tolerans ve boyut tavanı
/// TEK yerde. Sayfalar kendi küçük durum record'unu (ör. <c>CurrentTransactionTabState</c>) tanımlar,
/// açılışta <see cref="TryRead{T}"/> ile okur, değişiklik anında <see cref="Write{T}"/> ile iter.
/// KURAL: yalnız görünüm/filtre/kimlik; kaydedilmemiş form verisi (model DTO'su) ASLA.
/// </summary>
public static class TabPageState
{
    /// <summary>PageState JSON tavanı (~16 KB). Görünüm durumu küçüktür; aşan yazım bir sızıntı işaretidir
    /// (yanlışlıkla koca listeyi/DTO'yu durum sanmak) → yazılmaz, loglanır.</summary>
    public const int MaxJsonLength = 16 * 1024;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    /// <summary>Restore edilen sekmenin durumunu okur. Sekme yoksa, durum yoksa, JSON bozuksa/eski
    /// şemadaysa null → sayfa taze varsayılanlarla açılır (fail-safe; kullanıcıya hata gösterilmez).</summary>
    public static T? TryRead<T>(IMdiTab? tab) where T : class
    {
        if (string.IsNullOrWhiteSpace(tab?.PageState)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(tab.PageState, _options);
        }
        catch (JsonException)
        {
            return null;   // eski/bozuk kayıt → taze başlangıç
        }
    }

    /// <summary>Sayfanın güncel görünüm durumunu sekmeye yazar (kalıcılaşır). Tavan aşımında yazmaz,
    /// yalnız loglar — durum kaybı sayfayı bozmaz, sadece restore varsayılana düşer.</summary>
    public static void Write<T>(IMdiTabOpener opener, IMdiTab? tab, T state, ILogger? logger = null) where T : class
    {
        if (tab is null) return;   // popup/pop-out kipinde sekme yok → no-op

        var json = JsonSerializer.Serialize(state, _options);
        if (json.Length > MaxJsonLength)
        {
            logger?.LogWarning(
                "PageState yazımı atlandı: {Type} {Length} karakter (tavan {Max}) — görünüm durumuna veri sızıyor olabilir.",
                typeof(T).Name, json.Length, MaxJsonLength);
            return;
        }
        opener.UpdateTabState(tab.Id, json);
    }

    /// <summary>Sekmenin durumunu temizler (ör. kullanıcı filtreleri sıfırlayınca).</summary>
    public static void Clear(IMdiTabOpener opener, IMdiTab? tab)
    {
        if (tab is null) return;
        opener.UpdateTabState(tab.Id, null);
    }
}
