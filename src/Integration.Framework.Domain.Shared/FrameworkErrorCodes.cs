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
}
