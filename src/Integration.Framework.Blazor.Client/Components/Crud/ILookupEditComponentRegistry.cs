using System;
using System.Collections.Generic;

namespace Integration.Framework.Blazor.Client.Components.Crud;

/// <summary>
/// Lookup listesi DTO'su → o kaydın DÜZENLEME bileşeni eşlemesi.
///
/// <para><b>Neden var (2026-08-07 Hakan kuralı):</b> <c>LookupComboBox</c>'ın varlık sebebi ekle/düzelt
/// düğmeleridir — <i>"yoksa standart combo zaten işimizi çok rahat görüyor"</i>. Ama düğmeler yalnız çağıran
/// <c>EditComponentType</c> verdiğinde çiziliyordu ve 69 kullanımın ancak 15'i veriyordu: geri kalan her lookup
/// sessizce düz bir combo'ya iniyordu. Kullanıcı için bu bir hata gibi görünmez, yalnız "buradan yeni birim
/// ekleyemiyorum" diye biten bir iş akışı olur.</para>
///
/// <para><b>Çözüm çağrı yerlerini yamamak DEĞİL</b> (50+ dosyaya aynı satırı yazmak, birini unutmayı garanti
/// eder): hedef TİPTEN çözülür. Çağıran hâlâ <c>EditComponentType</c> ile ezebilir; kayıt defteri yalnız
/// VARSAYILANI sağlar.</para>
/// </summary>
public interface ILookupEditComponentRegistry
{
    /// <summary>Liste DTO tipinin düzenleme bileşeni; eşleşme yoksa <c>null</c>.</summary>
    Type? Resolve(Type itemType);
}

/// <summary>Sözlük tabanlı kayıt defteri — uygulama başlangıçta kendi eşlemesini verir (Framework hiçbir
/// uygulama tipini TANIMAZ).</summary>
public class LookupEditComponentRegistry : ILookupEditComponentRegistry
{
    private readonly IReadOnlyDictionary<Type, Type> _map;

    public LookupEditComponentRegistry(IReadOnlyDictionary<Type, Type> map)
    {
        _map = map;
    }

    public Type? Resolve(Type itemType)
    {
        return _map.TryGetValue(itemType, out var editComponent) ? editComponent : null;
    }
}
