using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün istemcisi — Trendyol Marketplace API v2 (REST/JSON, apigw.trendyol.com). Ürün oluşturma ASENKRON:
/// <see cref="SubmitProductAsync"/> bir <c>batchRequestId</c> döner; sonuç ayrıca <see cref="GetBatchStatusAsync"/>
/// ile sorgulanır. Model ÇÖZÜLMÜŞ gelir; client yalnız JSON serialize/parse eder.
/// <para><b>DİKKAT:</b> Bu istemci Trendyol'un KAMUYA AÇIK dokümante API'sine göre yazılmıştır ancak bu oturumda
/// CANLI DOĞRULANMAMIŞTIR (Trendyol satıcı test kimliği yok). Endpoint/alan varsayımları <see cref="TrendyolProductClient"/>
/// başındaki nota göre gerçek Trendyol dokümanıyla teyit edilmeli + gerçek kimlikle test edilmelidir.</para>
/// </summary>
public interface ITrendyolProductClient
{
    /// <summary>Ürünü Trendyol'a gönderir (async create). Batch id döner; başarısızsa BusinessException fırlatır.</summary>
    Task<TrendyolSubmitResult> SubmitProductAsync(TrendyolProductData product, TrendyolCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Bir batch isteğinin durumunu sorgular (COMPLETED/FAILED + başarısız kalem gerekçeleri).</summary>
    Task<TrendyolBatchStatus> GetBatchStatusAsync(string batchRequestId, TrendyolCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>Trendyol kanal kimliği (SellerId + ApiKey + ApiSecret) — Basic auth + User-Agent için.</summary>
public sealed record TrendyolCredentials(string SellerId, string ApiKey, string ApiSecret);

/// <summary>Trendyol ürün verisi (ÇÖZÜLMÜŞ) — ürün başlığı/kategori/marka + varyantlar (Items) + kategori attribute'ları.</summary>
public sealed record TrendyolProductData(
    string ProductMainId,     // varyantları gruplar (Ürün.Code)
    string Title,
    string Description,
    string CategoryId,        // numerik
    string BrandId,           // numerik
    int VatRate,
    int? CargoCompanyId,
    decimal? DimensionalWeight,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<TrendyolAttributeValue> Attributes,   // kategori attribute (id-bazlı)
    IReadOnlyList<TrendyolProductItem> Items);          // varyantlar (barcode başına)

/// <summary>Trendyol satılabilir kalem (= ERP varyantı) — barcode + stok + fiyat.</summary>
public sealed record TrendyolProductItem(
    string Barcode,
    string StockCode,
    int Quantity,
    decimal ListPrice,
    decimal SalePrice,
    string CurrencyType);     // "TRY"

/// <summary>Trendyol attribute değeri (id-bazlı) — value id ile listeden ya da customValue ile serbest.</summary>
public sealed record TrendyolAttributeValue(int AttributeId, int? AttributeValueId, string? CustomValue);

/// <summary>Submit yanıtı — Trendyol'un döndürdüğü batch istek kimliği (durum bununla sorgulanır).</summary>
public sealed record TrendyolSubmitResult(string? BatchRequestId);

/// <summary>Batch durum sorgusu sonucu — durum + kalem sayısı + başarısız kalem gerekçeleri (birleştirilmiş).</summary>
public sealed record TrendyolBatchStatus(string? Status, int ItemCount, int FailedCount, string? FailureReasons);
