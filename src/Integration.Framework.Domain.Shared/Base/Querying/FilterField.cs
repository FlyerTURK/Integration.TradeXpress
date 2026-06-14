namespace Integration.Framework.Base.Querying;

/// <summary>
/// Tek bir kolon filtresi: alan + operatör + (metinleştirilmiş) değer.
/// Değer string taşınır; sunucu tarafında alanın gerçek tipine güvenli
/// şekilde çevrilir (bkz. ListQueryableExtensions).
/// </summary>
public class FilterField
{
    public string Field { get; set; } = string.Empty;

    public ListFilterOperator Operator { get; set; } = ListFilterOperator.Contains;

    public string? Value { get; set; }
}
