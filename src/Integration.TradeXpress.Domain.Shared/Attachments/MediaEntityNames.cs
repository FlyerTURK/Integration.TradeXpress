namespace Integration.TradeXpress.Attachments;

/// <summary>
/// EntityMedia link anahtarları (<c>EntityName</c>) — TEK KAYNAK.
///
/// <para><b>Neden sabit:</b> aynı dizeyi YAZAN (edit formu → <c>ReplaceForAsync</c>) ve OKUYAN (pazaryeri push'u
/// → <c>GetPushMediaAsync</c>) taraflar farklı katmanlarda. Tek harf sapması istisna fırlatmaz; medya sessizce
/// "yok" görünür ve pazaryerine görselsiz ürün gider. Bu yüzden dize serbest yazılmaz.</para>
/// </summary>
public static class MediaEntityNames
{
    /// <summary>Ürün-seviyesi medya (görsel + video kütüphanesi).</summary>
    public const string Product = "Product";

    /// <summary>VARYANT-seviyesi ürün medyası — varyantın Id'siyle eşleşir.
    ///
    /// <para>Emtia ailelerinin (<c>GoodVariant</c>/<c>JewelryVariant</c>/<c>MetalVariant</c>) yıllardır kullandığı
    /// desenin ürün karşılığı: varyanta özel medya, kayıt geneli medyadan AYRI bağlamda durur. Aynı anahtarı
    /// doküman ve not servisleri de paylaşır — bu yüzden link üzerinde ayrı bir varyant kolonu YOKTUR.</para></summary>
    public const string ProductVariant = "ProductVariant";
}
