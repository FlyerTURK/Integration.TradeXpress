using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 ürün istemcisi — SOAP ProductService.SaveProduct (kanalın KENDİ kimliğiyle). Ürünü + varyantlarını (stockItems)
/// + kategori (leaf) + attribute'lar + kargo şablonu + condition + Seyahat özel bilgisi N11'e gönderir/günceller
/// (<see cref="N11ProductData.ProductSellerCode"/> upsert kimliği). Model ÇÖZÜLMÜŞ gelir; client yalnız XML serialize/parse eder.
/// </summary>
public interface IN11ProductClient
{
    /// <summary>Ürünü N11'e oluşturur/günceller. Başarısızsa BusinessException fırlatır (çağıran senkron durumunu işaretler).</summary>
    Task<N11SaveProductResult> SaveProductAsync(N11ProductData product, string appKey, string appSecret, CancellationToken cancellationToken = default);

    /// <summary>Ürünü N11'den okur (GetProductByProductId) — push sonrası doğrulama/eşitleme okuması
    /// (N11 kuralları kendi tarafında oynatabildiğinden yerel kayıt N11 GERÇEĞİYLE eşlenir; 2026-07-07 kararı).</summary>
    Task<N11ProductDetail> GetProductAsync(long n11ProductId, string appKey, string appSecret, CancellationToken cancellationToken = default);
}

/// <summary>N11 SaveProduct — ÇÖZÜLMÜŞ ürün verisi (fiyat/kategori/attribute/stockItems dolu); XML'e serialize edilir.</summary>
public sealed record N11ProductData(
    string ProductSellerCode,
    string Title,
    string Description,
    bool Domestic,
    string CategoryId,
    decimal Price,
    int CurrencyType,          // 1 = TL
    byte ProductCondition,     // 1 = Yeni, 2 = İkinci El
    int PreparingDay,
    string ShipmentTemplate,
    int? MaxPurchaseQuantity,
    IReadOnlyList<N11ProductImage> Images,
    IReadOnlyList<N11ProductAttributePair> Attributes,     // kategori attribute (name/value)
    IReadOnlyList<N11ProductStockItem> StockItems,
    IReadOnlyList<N11ProductSpecialInfo> SpecialInfo);     // Seyahat kategorisi (key/value)

/// <summary>Ürün görseli (url + sıra).</summary>
public sealed record N11ProductImage(string Url, int Order);

/// <summary>N11 attribute çifti (name/value) — kategori attribute'u ya da varyant option'u.</summary>
public sealed record N11ProductAttributePair(string Name, string Value);

/// <summary>Seyahat özel bilgisi (key=TurProgrami/IptalIadeKosullari/EkHizmetler, value=HTML).</summary>
public sealed record N11ProductSpecialInfo(string Key, string Value);

/// <summary>N11 stok birimi (= ERP varyantı). Seçenekliyse attributes (name/value serbest).</summary>
public sealed record N11ProductStockItem(
    string SellerStockCode,
    int Quantity,
    decimal? OptionPrice,
    IReadOnlyList<N11ProductAttributePair> Attributes,
    string? Gtin,
    string? Mpn);

/// <summary>SaveProduct yanıtı — N11'in atadığı ürün id'si + durumlar + SKU kimlikleri (stockItems yanıt bloğundan).</summary>
public sealed record N11SaveProductResult(
    long? N11ProductId,
    string? SellerCode,
    string? SaleStatus,
    string? ApprovalStatus,
    IReadOnlyList<N11SkuIdentity> Skus);

/// <summary>Yanıttaki SKU kimliği — N11 SKU id + version (fiyat/adet değişiminde artar; drift sinyali).
/// <see cref="SellerStockCode"/> yerel satır eşleme anahtarıdır.</summary>
public sealed record N11SkuIdentity(string SellerStockCode, long? N11SkuId, long? Version);

/// <summary>GetProductByProductId yanıtı — N11'in NORMALİZE ETTİĞİ ürün gerçeği (eşitleme okuması). Alan null =
/// yanıtta yok/boş (çağıran o alana DOKUNMAZ — N11'in desteklemediği alan yereldeki değeri silmesin).</summary>
public sealed record N11ProductDetail(
    long N11ProductId,
    string? Title,
    string? CategoryId,
    string? CategoryName,          // fullName (tam yol) tercih; yoksa name
    string? ShipmentTemplate,
    byte? ProductCondition,        // 1=Yeni, 2=İkinci El (parse edilemezse null)
    int? PreparingDay,
    int? MaxPurchaseQuantity,
    string? SaleStatus,
    string? ApprovalStatus,
    IReadOnlyList<N11ProductAttributePair>? Attributes,    // null = blok yok; BOŞ liste de "bilgi yok" sayılır (uygulayıcı silmez)
    IReadOnlyList<N11SkuIdentity> Skus);                   // yanıttaki stockItems kimlikleri (yoksa boş)
