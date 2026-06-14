namespace Integration.Framework.Base.Querying;

/// <summary>
/// Kolon filtresi operatörleri. UI (DevExpress) tarafından üretilen filtre,
/// presentation adapter'ında bu nötr operatörlere çevrilir; sunucu yalnız
/// bunları tanır (vendor tipi sunucuya sızmaz).
/// </summary>
public enum ListFilterOperator
{
    Equals,
    NotEquals,
    Contains,
    StartsWith,
    EndsWith,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}
