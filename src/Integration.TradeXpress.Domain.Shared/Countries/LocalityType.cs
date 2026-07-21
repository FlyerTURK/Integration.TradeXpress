namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülkenin yerellik (idari-alan altı) adres etiketi tipi — Google libaddressinput
/// <c>locality_name_type</c> karşılığı. Picker ilçe/şehir combo'sunun başlığı bu tipe göre uyarlanır
/// (TR→İlçe, US→Şehir). İlk üye (<see cref="City"/>=0) generic varsayılan.
/// </summary>
public enum LocalityType
{
    /// <summary>Şehir (generic varsayılan — US dâhil çoğu ülke).</summary>
    City = 0,

    /// <summary>İlçe (TR).</summary>
    District,

    /// <summary>Posta şehri (İngiliz "post town").</summary>
    PostTown,

    /// <summary>Semt (bazı ülkelerde yerellik seviyesi).</summary>
    Suburb,
}
