using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// <see cref="TrendyolProductClient.FetchAllPagesAsync"/> birim testleri — sayfalama döngüsü sahte sayfa kaynağıyla
/// (ağ/DI yok): çok-sayfalı birleştirme, totalPages'e saygı ve güvenlik tavanı aşımında dostane hata (sessiz kısmî
/// sonuç YOK). Import testlerindeki sahte istemci bu döngüyü atladığından gerçek döngü BURADA doğrulanır.
/// </summary>
public class TrendyolProductClientPagingTests
{
    [Fact]
    public async Task FetchAllPages_merges_items_of_all_pages_in_order()
    {
        var fetchedPages = new List<int>();

        var flat = await TrendyolProductClient.FetchAllPagesAsync(page =>
        {
            fetchedPages.Add(page);
            var items = new List<TrendyolRemoteProduct> { BuildItem($"BR-{page}") };
            return Task.FromResult(new TrendyolSellerProductsPage(page, 200, TotalPages: 3, TotalElements: 3, items));
        });

        fetchedPages.ShouldBe(new[] { 0, 1, 2 });   // totalPages=3 → sayfa 3 İSTENMEZ
        flat.Select(p => p.Variants.Single().Barcode).ShouldBe(new[] { "BR-0", "BR-1", "BR-2" });
    }

    [Fact]
    public async Task FetchAllPages_returns_empty_when_first_page_reports_zero_total_pages()
    {
        var flat = await TrendyolProductClient.FetchAllPagesAsync(page =>
            Task.FromResult(new TrendyolSellerProductsPage(page, 200, TotalPages: 0, TotalElements: 0, new List<TrendyolRemoteProduct>())));

        flat.ShouldBeEmpty();
    }

    // Bozuk/aşırı totalPages güvenlik tavanına takılırsa SESSİZCE kısmî liste dönülmez — dostane hata
    // (import upsert-only olduğundan yeniden deneme güvenli; raporsuz eksik güncelleme yasak).
    [Fact]
    public async Task FetchAllPages_throws_friendly_error_when_safety_page_limit_is_exceeded()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => TrendyolProductClient.FetchAllPagesAsync(page =>
            Task.FromResult(new TrendyolSellerProductsPage(page, 200, TotalPages: int.MaxValue, TotalElements: 0, new List<TrendyolRemoteProduct>()))));

        ex.Code.ShouldBe("TradeXpress:Trendyol:Product:PageLimitExceeded");
    }

    private static TrendyolRemoteProduct BuildItem(string barcode)
    {
        return new TrendyolRemoteProduct(
            ProductMainId: null,
            Title: $"Kalem {barcode}",
            Description: null,
            CategoryId: null,
            CategoryName: null,
            BrandId: null,
            BrandName: null,
            VatRate: null,
            DimensionalWeight: null,
            DeliveryDuration: null,
            ImageUrls: new List<string>(),
            Variants: new List<TrendyolRemoteVariant>
            {
                new(
                    Barcode: barcode,
                    StockCode: null,
                    Quantity: 1,
                    ListPrice: null,
                    SalePrice: null,
                    ProductContentId: null,
                    Approved: null,
                    OnSale: null,
                    Attributes: new List<TrendyolRemoteAttribute>()),
            });
    }
}
