namespace Integration.Framework.Addressing;

/// <summary>
/// <see cref="Address"/> VO'nun UBL <c>cac:PostalAddress</c> projeksiyonu — fatura/e-fatura dilimine hazır, salt-veri
/// taşıyıcı (küçük, davranışsız record). Alan adları UBL rolleridir; kaynak eşleme <see cref="Address.ToUblPostalAddress"/>'te.
///
/// <para><b>Türkçe idari kademe → UBL rol eşlemesi</b> (onaylı, 2026-07-21): İl → <c>CityName</c> ·
/// İlçe → <c>CitySubdivisionName</c> · Mahalle → <c>District</c> · Cadde/Sokak → <c>StreetName</c>.</para>
/// </summary>
/// <param name="StreetName">UBL <c>cbc:StreetName</c> — cadde/sokak (Address.Line).</param>
/// <param name="AdditionalStreetName">UBL <c>cbc:AdditionalStreetName</c> — ek cadde adı (Address.AdditionalStreetName).</param>
/// <param name="BuildingName">UBL <c>cbc:BuildingName</c> — bina adı (Address.BuildingName).</param>
/// <param name="BuildingNumber">UBL <c>cbc:BuildingNumber</c> — bina no (Address.BuildingNumber).</param>
/// <param name="Room">UBL <c>cbc:Room</c> — oda/daire (Address.Room).</param>
/// <param name="Floor">UBL <c>cbc:Floor</c> — kat (Address.Floor).</param>
/// <param name="Postbox">UBL <c>cbc:Postbox</c> — posta kutusu (Address.Postbox).</param>
/// <param name="CitySubdivisionName">UBL <c>cbc:CitySubdivisionName</c> — ilçe (Address.District).</param>
/// <param name="CityName">UBL <c>cbc:CityName</c> — il (Address.City).</param>
/// <param name="PostalZone">UBL <c>cbc:PostalZone</c> — posta kodu (Address.PostalCode).</param>
/// <param name="District">UBL <c>cbc:District</c> — mahalle (Address.Neighborhood).</param>
/// <param name="CountrySubentityCode">UBL <c>cbc:CountrySubentityCode</c> — ISO 3166-2 (Address.AdministrativeAreaIsoCode).</param>
/// <param name="CountryIdentificationCode">UBL <c>cac:Country/cbc:IdentificationCode</c> — ISO 3166-1 alpha-2 (Address.CountryCode).</param>
public record UblPostalAddress(
    string StreetName,
    string? AdditionalStreetName,
    string? BuildingName,
    string? BuildingNumber,
    string? Room,
    string? Floor,
    string? Postbox,
    string? CitySubdivisionName,
    string CityName,
    string? PostalZone,
    string? District,
    string? CountrySubentityCode,
    string CountryIdentificationCode);
