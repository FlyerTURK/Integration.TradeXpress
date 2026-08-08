using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Trendyol;
using Volo.Abp;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>
/// Trendyol ürün REST istemcisinin TEST sahtesi — testte ağ yok (READ-ONLY pazaryeri ilkesinin test aynası).
/// <see cref="RemoteItems"/>'a konan DÜZ kalemler (barcode başına tek-varyantlı <see cref="TrendyolRemoteProduct"/>)
/// sayfa yanıtı gibi servis edilir; <see cref="GetAllSellerProductsAsync"/> GERÇEK sayfalama döngüsünü
/// (<see cref="TrendyolProductClient.FetchAllPagesAsync"/>) + GERÇEK gruplama mantığını
/// (<see cref="TrendyolProductClient.GroupByProductMainId"/>) kullanır — sahte yalnız ağı keser, davranışı değil.
/// Push uçları import testlerinde ÇAĞRILMAMALIDIR (çağrılırsa KIRMIZI — BusinessException).
///
/// <para><b>Fiyat/stok ucu VARSAYILAN OLARAK KAPALI</b> (<see cref="AllowPriceInventoryWrites"/>): kazayla yazma
/// yapan bir test kırmızıya döner. Yazma senaryosu kuran test bayrağı açıkça açar ve gönderilen satırları
/// <see cref="PriceInventoryBatches"/>'ten doğrular. Sentinel hata kodu ÜRETİM kodlarından AYRIDIR
/// (<c>TradeXpress:Test:*</c>) — aksi hâlde "kazayla sahteye ulaşıldı" ile "gerçek HTTP hatası" ayırt edilemezdi.</para>
/// </summary>
public sealed class FakeTrendyolProductClient : ITrendyolProductClient
{
    /// <summary>Sahte pazaryeri envanteri — düz kalemler (her öğe tek varyant taşır; gruplama GetAll'da).</summary>
    public List<TrendyolRemoteProduct> RemoteItems { get; } = new();

    /// <summary>Fiyat/stok yazma izni — VARSAYILAN <c>false</c> (okuma/import testleri yazma yapamaz).</summary>
    public bool AllowPriceInventoryWrites { get; set; }

    /// <summary>Gönderilen satır kümeleri (çağrı başına bir kayıt) — "ne gönderildi" bununla doğrulanır.</summary>
    public List<IReadOnlyList<TrendyolPriceInventoryItem>> PriceInventoryBatches { get; } = new();

    /// <summary>Sahtenin döndüreceği makbuz kimliği (test ayarlayabilir).</summary>
    public string? NextBatchRequestId { get; set; } = "BATCH-PI-1";

    public Task<TrendyolSubmitResult> SubmitProductAsync(
        TrendyolProductData product, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Trendyol:Product:SubmitFailed");
    }

    public Task<TrendyolSubmitResult> UpdatePriceAndInventoryAsync(
        IReadOnlyList<TrendyolPriceInventoryItem> items, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (!AllowPriceInventoryWrites)
        {
            throw new BusinessException("TradeXpress:Test:TrendyolWriteNotAllowed");
        }

        PriceInventoryBatches.Add(items);
        return Task.FromResult(new TrendyolSubmitResult(NextBatchRequestId));
    }

    /// <summary>Durum sorgusunun döneceği sonuç. <b>Varsayılan <c>null</c> = uç KAPALI</b> (fırlatır) —
    /// yani "kazayla durum sorgulandı" ile "test bilerek batch çözdü" ayırt edilebilir kalır.</summary>
    public TrendyolBatchStatus? NextBatchStatus { get; set; }

    public Task<TrendyolBatchStatus> GetBatchStatusAsync(
        string batchRequestId, TrendyolCredentials credentials, CancellationToken cancellationToken = default)
    {
        if (NextBatchStatus is null)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:StatusFailed");
        }

        return Task.FromResult(NextBatchStatus);
    }

    public Task<TrendyolSellerProductsPage> GetSellerProductsAsync(
        TrendyolCredentials credentials, int page, int size, CancellationToken cancellationToken = default)
    {
        var items = page == 0 ? RemoteItems.ToList() : new List<TrendyolRemoteProduct>();
        var totalPages = RemoteItems.Count == 0 ? 0 : 1;
        return Task.FromResult(new TrendyolSellerProductsPage(page, size, totalPages, RemoteItems.Count, items));
    }

    public async Task<IReadOnlyList<TrendyolRemoteProduct>> GetAllSellerProductsAsync(
        TrendyolCredentials credentials, int pageSize = 200, CancellationToken cancellationToken = default)
    {
        var flat = await TrendyolProductClient.FetchAllPagesAsync(
            page => GetSellerProductsAsync(credentials, page, pageSize, cancellationToken));
        return TrendyolProductClient.GroupByProductMainId(flat);
    }
}
