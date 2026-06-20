using System.Collections.Generic;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>Sayfa-aşırı gezinme kararının sonucu.</summary>
public enum NavKind { None, Local, CrossPage }

/// <summary>
/// Bir Previous/Next adımının nasıl gerçekleşeceği:
/// - <see cref="NavKind.None"/>: uçta (gidilecek kayıt yok).
/// - <see cref="NavKind.Local"/>: komşu YÜKLÜ sayfada → <see cref="LocalKey"/> ile lokal geçiş (server isteği yok).
/// - <see cref="NavKind.CrossPage"/>: komşu yüklü sayfa dışında → <see cref="TargetGlobalIndex"/>'teki kaydı
///   tek-kayıt sorgusuyla çek (split: grid'i o sayfaya getir; popup: o kaydı yükle).
/// </summary>
public readonly record struct NavOutcome(NavKind Kind, object? LocalKey, int TargetGlobalIndex)
{
    public static readonly NavOutcome None = new(NavKind.None, null, -1);
    public static NavOutcome Local(object key) => new(NavKind.Local, key, -1);
    public static NavOutcome CrossPage(int target) => new(NavKind.CrossPage, null, target);
}

/// <summary>
/// Sayfa-aşırı (server-side, tüm kayıtlar arası) Previous/Next için SAF karar yardımcısı (UI'sız).
/// "Geçerli kayıt kaynağı" ve "geçiş biçimi" çağıran bağlamda kalır (split: remount+focus; popup: LoadData);
/// burada yalnız "komşu yüklü sayfada mı, yoksa sayfa-aşırı mı, yoksa uçta mı" kararı verilir.
/// <see cref="RecordNavigation"/>'ın global-index kardeşi.
/// </summary>
public static class CrossPageNavigator
{
    /// <param name="previous">true=önceki, false=sonraki.</param>
    /// <param name="globalIndex">Geçerli kaydın tüm kayıtlar içindeki sırası (yoksa -1).</param>
    /// <param name="totalCount">Sunucudaki toplam kayıt.</param>
    /// <param name="pageSkip">Yüklü sayfanın SkipCount'u (ilk yerel kaydın global sırası).</param>
    /// <param name="loadedKeys">Yüklü sayfanın sıralı anahtarları (Id'leri).</param>
    public static NavOutcome Resolve(bool previous, int globalIndex, long totalCount, int pageSkip, IReadOnlyList<object> loadedKeys)
    {
        if (globalIndex < 0) return NavOutcome.None;
        // Uç kontrol (tüm kayıtlar düzeyinde)
        if (previous ? globalIndex <= 0 : globalIndex >= totalCount - 1) return NavOutcome.None;

        var target = previous ? globalIndex - 1 : globalIndex + 1;
        var localTarget = target - pageSkip;   // komşunun yüklü sayfadaki konumu

        if (loadedKeys != null && localTarget >= 0 && localTarget < loadedKeys.Count)
            return NavOutcome.Local(loadedKeys[localTarget]);   // yüklü sayfada → server isteği yok

        return NavOutcome.CrossPage(target);   // sayfa sınırını aşıyor
    }
}
