using System;

namespace Integration.Framework.Blazor.Client.Services.Mdi;

/// <summary>Bir entity üzerinde gerçekleşen değişim türü (satır-düzeyi güncelleme tüketicileri için).</summary>
public enum EntityChangeKind
{
    /// <summary>Tür-genel "değişti" — id bilinmiyor/önemsiz (tüketici tam reload yapar).</summary>
    Unspecified,
    Created,
    Updated,
    Deleted
}

/// <summary>
/// Bir entity değişim bildirimi. <see cref="EntityKey"/> tür anahtarı (ör. "admin/users"),
/// <see cref="Kind"/> değişim türü, <see cref="Id"/> değişen kaydın anahtarı (varsa).
/// </summary>
public sealed record EntityChange(string EntityKey, EntityChangeKind Kind, object? Id);

/// <summary>
/// Sekmeler arası (MDI) / popup ↔ liste hafif pub/sub — bir edit kaydedince ilgili liste(ler)
/// kendini yeniler. Aynı devre (circuit) içinde scoped tek örnek paylaşılır.
/// İki katman: kaba <see cref="EntityChanged"/> (tür anahtarı → genelde tam reload) ve ince
/// <see cref="Changed"/> (id + tür → satır-düzeyi güncelleme; tüketici opsiyonel).
/// </summary>
public interface IEntityChangeNotifier
{
    /// <summary>Bir entity türü değiştiğinde tetiklenir; argüman entity anahtarıdır (ör. "admin/users").</summary>
    event Action<string>? EntityChanged;

    /// <summary>Değişimi id + tür ile bildirir (satır-düzeyi güncelleme isteyen tüketiciler için).</summary>
    event Action<EntityChange>? Changed;

    /// <summary>Verilen anahtar için kaba "değişti" bildirimi (create/update/delete sonrası, id'siz).</summary>
    void Notify(string entityKey);

    /// <summary>Verilen anahtar için ince bildirim: değişim türü + kaydın id'si.</summary>
    void Notify(string entityKey, EntityChangeKind kind, object? id);
}

public sealed class EntityChangeNotifier : IEntityChangeNotifier
{
    public event Action<string>? EntityChanged;
    public event Action<EntityChange>? Changed;

    public void Notify(string entityKey) => Notify(entityKey, EntityChangeKind.Unspecified, null);

    public void Notify(string entityKey, EntityChangeKind kind, object? id)
    {
        if (string.IsNullOrEmpty(entityKey)) return;
        // Yineleme sırasında yarış olmasın diye delegeler anlık kopyalanır.
        var detailed = Changed;
        detailed?.Invoke(new EntityChange(entityKey, kind, id));

        var coarse = EntityChanged;
        coarse?.Invoke(entityKey);   // geriye uyumluluk: mevcut tüketiciler (tam reload) çalışmaya devam eder
    }
}
