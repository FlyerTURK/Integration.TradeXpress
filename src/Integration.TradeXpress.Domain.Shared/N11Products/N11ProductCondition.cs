namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 ürün durumu (<c>productCondition</c>) — resmî SOAP dokümanı: <b>1=Yeni</b>, <b>2=İkinci El</b>.
/// Wire'a byte değeri string olarak yazılır ("1"/"2").
/// </summary>
public enum N11ProductCondition : byte
{
    /// <summary>Yeni ürün.</summary>
    New = 1,

    /// <summary>İkinci el / kullanılmış ürün.</summary>
    Used = 2,
}
