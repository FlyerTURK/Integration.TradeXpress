namespace Integration.Framework.Addressing;

/// <summary>
/// <see cref="Address"/> VO'nun UBL <c>cac:PostalAddress</c> projeksiyonu — fatura/e-fatura dilimine hazır, salt-veri
/// taşıyıcı (küçük, davranışsız record). Alan adları UBL rolleridir; kaynak eşleme <see cref="Address.ToUblPostalAddress"/>'te.
/// </summary>
/// <param name="StreetName">UBL <c>cbc:StreetName</c> — açık adres (Address.Line).</param>
/// <param name="CitySubdivisionName">UBL <c>cbc:CitySubdivisionName</c> — mahalle (Address.Neighborhood).</param>
/// <param name="CityName">UBL <c>cbc:CityName</c> — ilçe/şehir (Address.District).</param>
/// <param name="PostalZone">UBL <c>cbc:PostalZone</c> — posta kodu (Address.PostalCode).</param>
/// <param name="Region">UBL <c>cbc:CountrySubentity</c> — il/eyalet adı (Address.City).</param>
/// <param name="CountrySubentityCode">UBL <c>cbc:CountrySubentityCode</c> — ISO 3166-2 (Address.AdministrativeAreaIsoCode).</param>
/// <param name="CountryIdentificationCode">UBL <c>cac:Country/cbc:IdentificationCode</c> — ISO 3166-1 alpha-2 (Address.CountryCode).</param>
public record UblPostalAddress(
    string StreetName,
    string? CitySubdivisionName,
    string? CityName,
    string? PostalZone,
    string Region,
    string? CountrySubentityCode,
    string CountryIdentificationCode);
