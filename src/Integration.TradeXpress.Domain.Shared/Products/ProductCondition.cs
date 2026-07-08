namespace Integration.TradeXpress.Products;

/// <summary>
/// Ürün durumu — pazaryeri-genel varsayılan (N11 <c>productCondition</c>'ın kaynağı ama ONDAN BAĞIMSIZ; her
/// pazaryerine kendi karşılığına eşlenir). Ürün-seviyesi varsayılan; kanal-ürünü kendi durumunu override eder.
/// </summary>
public enum ProductCondition
{
    /// <summary>Yeni ürün.</summary>
    New = 0,

    /// <summary>İkinci el / kullanılmış ürün.</summary>
    Used = 1,
}
