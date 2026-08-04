using System.Collections.Generic;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// <c>GET /ms/product-query</c> filtresi (resmî doküman v9.0 "3.6 Satıcı Ürünlerini Listeleme").
/// <b>Hiçbir parametre zorunlu DEĞİL</b> — hepsi boş bırakılırsa satıcının TÜM ürünleri sayfalanır.
/// <list type="bullet">
///   <item><see cref="Size"/> <b>maksimum 50</b> (N11 varsayılanı 20). İstemci daha büyüğünü <b>sessizce 50'ye kırpar</b>
///   — doküman sınırı; aşan istek N11 tarafında kendi varsayılanına düşürülebilir/ret alabilir.</item>
///   <item><see cref="StockCode"/> istek başına <b>TEK</b> değer alır. Çoklu SKU sorgusu için SKU başına ayrı istek şart.</item>
///   <item><see cref="SaleStatus"/> ∈ {Before_Sale, On_Sale, Out_Of_Stock, Sale_Closed}.</item>
///   <item><see cref="ProductStatus"/> ∈ {Active, InCatalogApproval, Suspended, CatalogRejected, Unlisted, Prohibited, InApproval}.
///   ⚠ Bu <b>istek</b> parametresinin adıdır; <b>yanıtta</b> aynı bilgi <c>status</c> alanında gelir (adlar farklı).</item>
///   <item><see cref="CategoryIds"/> virgülle ayrılmış kategori id listesi (ör. <c>"1001,1002"</c>).</item>
/// </list>
/// Boş/null bırakılan alanlar sorgu dizesine <b>hiç yazılmaz</b>.
/// </summary>
public sealed record N11ProductQueryFilter(
    int Page,
    int Size,
    string? StockCode,
    string? SaleStatus,
    string? ProductStatus,
    string? BrandName,
    string? CategoryIds);

/// <summary>
/// <c>GET /ms/product-query</c> tek sayfa yanıtı (Spring Data <c>Page</c> şekli: <c>content</c> + <c>number</c> +
/// <c>totalPages</c> + <c>totalElements</c>).
/// <para><see cref="Page"/> yanıttaki <c>number</c> (0-tabanlı mevcut sayfa). <see cref="TotalPages"/> toplam sayfa,
/// <see cref="TotalCount"/> toplam kayıt (<c>totalElements</c> — ürün sayısı doğal olarak büyüdüğü için <c>long</c>).</para>
/// <para><b>Son sayfa tespiti:</b> doküman <c>content</c> boş dönen sayfayı son kabul etmeyi söyler; <see cref="TotalPages"/>
/// ikincil ölçüttür. İkisi de kullanılır (<c>QueryAllAsync</c>).</para>
/// </summary>
public sealed record N11RestProductPage(
    IReadOnlyList<N11RestProductSummary> Items,
    int Page,
    int TotalPages,
    long TotalCount);

/// <summary>
/// <c>GET /ms/product-query</c> yanıtındaki tek ürün satırı.
/// <para><see cref="ImageUrls"/> — yanıt GÖRSEL TAŞIR. (Eskiden buraya "yanıt görsel taşımaz" yazılmıştı; o tespit
/// v9.0 SOAP dokümanına dayanıyordu ve REST için YANLIŞTI. Resmî REST dokümanı 2026-02-04 hem alan tablosunda
/// (satır 1083: <c>imageUrls | Görsel Linkleri</c>) hem örnek yanıtta (satır 1152) veriyor.) Bu liste doğrudan
/// <c>MarketplaceImageDownloader</c>'a beslenebilir — mağaza içe aktarımı görselsiz kalmak zorunda değil.</para>
/// <para>⚠ <see cref="N11ProductId"/> <b>long</b>'dur: doküman "n11ProductId alanı 9 haneden 10 haneye çıkabilir" diye
/// açıkça uyarır — <c>int</c> kullanmak zamanla taşar. Yanıtta yoksa <c>0</c> kalır.</para>
/// <para><see cref="StockCode"/> satıcı SKU kodu = <b>yerel eşleme anahtarı</b>; yanıtta yoksa boş string (istemci
/// patlamaz, kararı çağıran verir). <see cref="ProductStatus"/> yanıttaki <c>status</c> alanından okunur.
/// Diğer tüm alanlar yanıtta yoksa <c>null</c> bırakılır.</para>
/// </summary>
public sealed record N11RestProductSummary(
    long N11ProductId,
    string? ProductMainId,
    string StockCode,
    string? Title,
    decimal? SalePrice,
    decimal? ListPrice,
    int? Quantity,
    string? SaleStatus,
    string? ProductStatus,
    string? CategoryId,
    IReadOnlyList<string> ImageUrls);
