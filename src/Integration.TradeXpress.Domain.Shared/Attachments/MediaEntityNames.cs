using System.Collections.Generic;

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

    public const string Good = "Good";
    public const string GoodVariant = "GoodVariant";

    public const string Jewelry = "Jewelry";
    public const string JewelryVariant = "JewelryVariant";

    public const string Metal = "Metal";
    public const string MetalVariant = "MetalVariant";

    public const string Stone = "Stone";
    public const string StoneVariant = "StoneVariant";

    /// <summary>
    /// Medya taşıyan TÜM kayıt tipleri, <b>bağlam ÇİFTİ</b> olarak — CLAUDE.md §6 "her medya tipi İKİ bağlamı da
    /// taşır" kuralının makine-okunur hâli.
    ///
    /// <para><b>Neden çift:</b> genel görsel KAYIT seviyesinde, farklılık görselleri VARYANTTA durur; push zinciri
    /// varyant→kayıt fallback'iyle okur (<c>MarketplacePushImageResolver</c>), yani ikisi de meşrudur ve biri
    /// diğerinin yerine geçmez. Tek bağlamı bağlayıp diğerini unutmak istisna fırlatmaz — medya sessizce "yok"
    /// görünür. Canlıda tam bu oldu: 185 medya bağının tamamı <c>Product</c> bağlamındaydı, <c>ProductVariant</c>'ta
    /// sıfır, Good'da ise ne DTO alanı ne panel vardı; "ürünün projeksiyonu" (<c>ProjectToGoodAsync</c>) olması
    /// gereken mamül formu görselsiz açılıyordu.</para>
    ///
    /// <para>Yeni bir medya tipi eklerken buraya da satır ekle — <c>MediaContextPairingTests</c> listeyi okuyup
    /// her çiftin gerçekten bağlandığını doğrular.</para>
    /// </summary>
    public static readonly IReadOnlyList<MediaContextPair> Registered = new[]
    {
        new MediaContextPair(Product, ProductVariant),
        new MediaContextPair(Good, GoodVariant),
        new MediaContextPair(Jewelry, JewelryVariant),
        new MediaContextPair(Metal, MetalVariant),
        new MediaContextPair(Stone, StoneVariant),
    };
}

/// <summary>Bir medya tipinin iki bağlamı: kayıt geneli + varyant farkı.</summary>
public sealed record MediaContextPair(string Record, string Variant);
