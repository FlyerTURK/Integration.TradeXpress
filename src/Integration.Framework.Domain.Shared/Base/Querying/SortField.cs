namespace Integration.Framework.Base.Querying;

/// <summary>
/// Tek bir kolona göre sıralama isteği. Grid'den gelen çok-kolonlu sıralama
/// bu nesnelerin sıralı bir listesi olarak taşınır (ilki birincil sıralama).
/// </summary>
public class SortField
{
    /// <summary>Sıralanacak alan adı (List DTO property adı).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>true = azalan (DESC), false = artan (ASC).</summary>
    public bool Descending { get; set; }
}
