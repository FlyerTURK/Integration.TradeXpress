namespace Integration.TradeXpress.Countries;

/// <summary>
/// Ülkenin posta kodu adres etiketi tipi — Google libaddressinput <c>zip_name_type</c> karşılığı.
/// Posta kodu alanının başlığı bu tipe göre uyarlanır (TR→Posta Kodu, US→ZIP, IN→PIN, IE→Eircode).
/// İlk üye (<see cref="PostalCode"/>=0) generic varsayılan.
/// </summary>
public enum PostalCodeType
{
    /// <summary>Posta kodu (generic varsayılan — TR dâhil çoğu ülke).</summary>
    PostalCode = 0,

    /// <summary>ZIP (US).</summary>
    Zip,

    /// <summary>PIN (IN).</summary>
    Pin,

    /// <summary>Eircode (IE).</summary>
    Eircode,
}
