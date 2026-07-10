using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Categories;
using Volo.Abp;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 ürün SOAP istemcisinin TEST sahtesi — testte ağ yok; push planı (SaveProduct'a giden N11ProductData)
/// yakalanır ve karakterizasyon assert'leri bunun üzerinde çalışır. N11ProductId null döner → push-sonrası
/// doğrulama okuması (GetProductAsync) hiç tetiklenmez (test dışı dallar sade kalır).
/// </summary>
public sealed class FakeN11ProductClient : IN11ProductClient
{
    /// <summary>SaveProduct'a ulaşan push verileri (sıralı) — son push <see cref="LastSavedProduct"/>.</summary>
    public List<N11ProductData> SavedProducts { get; } = new();

    public N11ProductData? LastSavedProduct
    {
        get { return SavedProducts.Count > 0 ? SavedProducts[^1] : null; }
    }

    public Task<N11SaveProductResult> SaveProductAsync(N11ProductData product, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        SavedProducts.Add(product);
        var skus = product.StockItems
            .Select((s, i) => new N11SkuIdentity(s.SellerStockCode, 1000 + i, 1))
            .ToList();
        return Task.FromResult(new N11SaveProductResult(null, product.ProductSellerCode, "Active", "WaitingForApproval", skus));
    }

    public Task<N11ProductDetail> GetProductAsync(long n11ProductId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:N11:Product:RecordNotFound");
    }

    public Task<N11ProductDetail> GetProductBySellerCodeAsync(string sellerCode, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:N11:Product:RecordNotFound");
    }

    public Task<N11SaveProductResult> UpdateProductBasicAsync(N11ProductBasicUpdate update, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        var skus = update.StockItems
            .Select(s => new N11SkuIdentity(s.SellerStockCode, s.N11SkuId, 2))
            .ToList();
        return Task.FromResult(new N11SaveProductResult(update.N11ProductId, update.ProductSellerCode, "Active", "Approved", skus));
    }
}

/// <summary>
/// N11 kategori istemcisinin TEST sahtesi — push validasyonunun beklediği yaprak kategori tanımını bellekten verir
/// (varsayılan: "Renk" + "Beden" varyant eksenleri, serbest değerli). Testler <see cref="Leaf"/>'i değiştirebilir.
/// </summary>
public sealed class FakeN11CategoryClient : IN11CategoryClient
{
    /// <summary>Testlerin ortak yaprak kategori kimliği (reconcile testleriyle aynı).</summary>
    public const string DefaultCategoryExternalId = "1000846";

    public N11LeafAttributes Leaf { get; set; } = new(
        DefaultCategoryExternalId,
        "Test Kategori",
        new List<N11AttributeDef>
        {
            new("1", "Renk", false, true, true, null, new List<N11AttributeValue>()),
            new("2", "Beden", false, true, true, null, new List<N11AttributeValue>()),
        });

    public Task<IReadOnlyList<N11CategoryNode>> GetCategoryTreeAsync(string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<N11CategoryNode>>(new List<N11CategoryNode>());
    }

    public Task<N11LeafAttributes> GetLeafAttributesAsync(string categoryExternalId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Leaf);
    }
}
