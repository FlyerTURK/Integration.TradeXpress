namespace Integration.Framework;

/// <summary>
/// Framework katmanının ürettiği iş hatalarının merkezi kod tablosu. Her kod
/// <c>Integration.Framework:*</c> önekiyle namespace'lenir ki tüketici projelerin
/// kendi kodlarıyla çakışmasın.
/// </summary>
public static class FrameworkErrorCodes
{
    /// <summary>Liste sorgusu izin verilmeyen alan / sınır dışı şekil reddi (bkz. ListQueryException).</summary>
    public const string ListQueryRejected = "Integration.Framework:ListQueryRejected";

    /// <summary>Zorunlu alan boş. Data: Property.</summary>
    public const string PropertyRequired = "Integration.Framework:PropertyRequired";

    /// <summary>Alan minimum uzunluktan kısa. Data: Property, Min.</summary>
    public const string PropertyTooShort = "Integration.Framework:PropertyTooShort";

    /// <summary>Alan maksimum uzunluğu aştı. Data: Property, Max.</summary>
    public const string PropertyTooLong = "Integration.Framework:PropertyTooLong";

    /// <summary>Sayısal alan izinli aralık dışında. Data: Property, Min, Max.</summary>
    public const string PropertyOutOfRange = "Integration.Framework:PropertyOutOfRange";
}
