namespace Integration.TradeXpress.N11Shipments;

/// <summary>
/// N11 "Şartlı Kargo" eşiğinin birimi (panel: "TL / Miktar"). Resmî SOAP dokümanı v4.6: <c>feeCondition</c> koşul tipi
/// <b>1=ByPrice</b> (tutar/TL) · <b>2=ByUnit</b> (adet). Değer ByPrice'ta <c>feeConditionPrice</c>, ByUnit'te
/// <c>feeConditionUnit</c> alanında. Adres elementine gömülü; push ile YAZILABİLİR (CANLI DOĞRULANDI — doküman alan tablosu eksikmiş).
/// </summary>
public enum N11ConditionalShippingUnit : byte
{
    /// <summary>Tutar (TL) üzeri ücretsiz kargo — N11 <c>feeCondition=1</c> (ByPrice), değer <c>feeConditionPrice</c>.</summary>
    Amount = 1,

    /// <summary>Adet (miktar) üzeri ücretsiz kargo — N11 <c>feeCondition=2</c> (ByUnit), değer <c>feeConditionUnit</c>.</summary>
    Quantity = 2,
}
