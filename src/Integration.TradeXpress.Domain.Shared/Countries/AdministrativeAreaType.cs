namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülkenin idari-alan (ISO 3166-2 üst seviye) adres etiketi tipi — Google libaddressinput
/// <c>administrative_area_name_type</c> karşılığı. Picker il/eyalet combo'sunun başlığı bu tipe göre
/// uyarlanır (TR→İl, US→Eyalet). İlk üye (<see cref="Province"/>=0) generic varsayılan.
/// </summary>
public enum AdministrativeAreaType
{
    /// <summary>İl (generic varsayılan — TR dâhil çoğu ülke).</summary>
    Province = 0,

    /// <summary>Eyalet (US, AU, ...).</summary>
    State,

    /// <summary>Bölge (IT, CL, ...).</summary>
    Region,

    /// <summary>Prefektörlük (JP).</summary>
    Prefecture,

    /// <summary>Emirlik (AE).</summary>
    Emirate,

    /// <summary>İdari bölge (İngiliz "county").</summary>
    County,
}
