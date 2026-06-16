using System;

namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>
/// Sekmeler arası (MDI) hafif pub/sub — bir edit sekmesi kaydedince ilgili liste sekmesi(leri)
/// kendini yeniler. Aynı devre (circuit) içinde scoped tek örnek paylaşılır.
/// </summary>
public interface IEntityChangeNotifier
{
    /// <summary>Bir entity türü değiştiğinde tetiklenir; argüman entity anahtarıdır (ör. "admin/users").</summary>
    event Action<string>? EntityChanged;

    /// <summary>Verilen anahtar için "değişti" bildirimi yayınlar (create/update/delete sonrası).</summary>
    void Notify(string entityKey);
}

public sealed class EntityChangeNotifier : IEntityChangeNotifier
{
    public event Action<string>? EntityChanged;

    public void Notify(string entityKey)
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        // Yineleme sırasında yarış olmasın diye delege anlık kopyalanır.
        var handler = EntityChanged;
        handler?.Invoke(entityKey);
    }
}
