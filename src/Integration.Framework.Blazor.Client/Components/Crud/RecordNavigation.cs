using System.Collections.Generic;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Kayıt gezinme (Previous/Next) için UI'sız, SAF aritmetik yardımcı (DRY merkezi).
/// Hem SplitView (geçerli kayıt = _session.CurrentId, liste = grid'in görünür anahtarları) hem
/// popup/standalone edit (geçerli kayıt = StateService.SelectedItem.Id, liste = ListDataSource Id'leri)
/// bu fonksiyonları kullanır. "Geçerli kayıt kaynağı" ve "geçiş biçimi" çağıran bağlamda kalır;
/// burada YALNIZ index/komşu/uç-kontrol mantığı tek yerde toplanır.
/// </summary>
public static class RecordNavigation
{
    /// <summary>current anahtarının keys içindeki sırası; bulunamazsa -1 (boş liste / null current dahil).</summary>
    public static int IndexOf(IReadOnlyList<object>? keys, object? current)
    {
        if (keys == null || current == null) return -1;
        for (int i = 0; i < keys.Count; i++)
            if (Equals(keys[i], current)) return i;
        return -1;
    }

    /// <summary>Önceki kayda gidilebilir mi? (current listede var ve ilk eleman değil)</summary>
    public static bool CanGoPrevious(IReadOnlyList<object>? keys, object? current)
        => IndexOf(keys, current) > 0;

    /// <summary>Sonraki kayda gidilebilir mi? (current listede var ve son eleman değil)</summary>
    public static bool CanGoNext(IReadOnlyList<object>? keys, object? current)
    {
        var i = IndexOf(keys, current);
        return i >= 0 && i < keys!.Count - 1;
    }

    /// <summary>Önceki anahtar; uçta veya bulunamazsa null.</summary>
    public static object? PreviousKey(IReadOnlyList<object>? keys, object? current)
        => CanGoPrevious(keys, current) ? keys![IndexOf(keys, current) - 1] : null;

    /// <summary>Sonraki anahtar; uçta veya bulunamazsa null.</summary>
    public static object? NextKey(IReadOnlyList<object>? keys, object? current)
        => CanGoNext(keys, current) ? keys![IndexOf(keys, current) + 1] : null;
}
