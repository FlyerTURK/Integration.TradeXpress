namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülkenin alt-yerellik (yerellik altı) adres etiketi tipi — Google libaddressinput
/// <c>sublocality_name_type</c> karşılığı. Picker mahalle combo'sunun başlığı bu tipe göre uyarlanır
/// (TR→Mahalle). Yalnız <c>UsesSubLocality</c> ülkelerde görünür. İlk üye (<see cref="Neighborhood"/>=0) generic varsayılan.
/// </summary>
public enum SubLocalityType
{
    /// <summary>Mahalle (generic varsayılan — TR).</summary>
    Neighborhood = 0,

    /// <summary>Semt.</summary>
    Suburb,

    /// <summary>İlçe (alt-yerellik olarak kullanan ülkeler).</summary>
    District,

    /// <summary>Köy/Belde.</summary>
    VillageTownship,
}
