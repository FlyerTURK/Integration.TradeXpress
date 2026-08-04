using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.N11Categories;
using Integration.TradeXpress.N11Products.Rest;
using Volo.Abp;

namespace Integration.TradeXpress.N11Products;

/// <summary>
/// N11 Ã¼rÃ¼n SOAP istemcisinin TEST sahtesi â€” testte aÄŸ yok; push planÄ± (SaveProduct'a giden N11ProductData)
/// yakalanÄ±r ve karakterizasyon assert'leri bunun Ã¼zerinde Ã§alÄ±ÅŸÄ±r. N11ProductId null dÃ¶ner â†’ push-sonrasÄ±
/// doÄŸrulama okumasÄ± (GetProductAsync) hiÃ§ tetiklenmez (test dÄ±ÅŸÄ± dallar sade kalÄ±r).
/// </summary>
public sealed class FakeN11ProductClient : IN11ProductClient
{
    /// <summary>SaveProduct'a ulaÅŸan push verileri (sÄ±ralÄ±) â€” son push <see cref="LastSavedProduct"/>.</summary>
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
/// N11 kategori istemcisinin TEST sahtesi â€” push validasyonunun beklediÄŸi yaprak kategori tanÄ±mÄ±nÄ± bellekten verir
/// (varsayÄ±lan: "Renk" + "Beden" varyant eksenleri, serbest deÄŸerli). Testler <see cref="Leaf"/>'i deÄŸiÅŸtirebilir.
/// </summary>
public sealed class FakeN11CategoryClient : IN11CategoryClient
{
    /// <summary>Testlerin ortak yaprak kategori kimliÄŸi (reconcile testleriyle aynÄ±).</summary>
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

/// <summary>
/// N11 Ã¼rÃ¼n REST istemcisinin TEST sahtesi â€” push artÄ±k REST'ten gidiyor (SOAP Ã¼rÃ¼n uÃ§larÄ± N11 tarafÄ±nda
/// KAPATILDI). AÄŸa Ã§Ä±kÄ±lmaz; <c>product-create</c>'e ulaÅŸan SATIRLAR yakalanÄ±r ve karakterizasyon assert'leri
/// bunlarÄ±n Ã¼zerinde Ã§alÄ±ÅŸÄ±r.
///
/// <para>SOAP sahtesinden farkÄ± yapÄ±saldÄ±r: SOAP tek <c>N11ProductData</c> alÄ±rdÄ±, REST her SKU iÃ§in AYRI satÄ±r
/// alÄ±r â€” testler de artÄ±k satÄ±r listesi Ã¼zerinden doÄŸrular.</para>
/// </summary>
public sealed class FakeN11ProductRestClient : IN11ProductRestClient
{
    /// <summary>product-create'e ulaÅŸan satÄ±r kÃ¼meleri (her push bir kÃ¼me).</summary>
    public List<IReadOnlyList<N11RestProductCreate>> CreatedBatches { get; } = new();

    /// <summary>Son push'un satÄ±rlarÄ± â€” Ã§oÄŸu test bunu okur.</summary>
    public IReadOnlyList<N11RestProductCreate> LastCreatedRows =>
        CreatedBatches.Count > 0 ? CreatedBatches[^1] : Array.Empty<N11RestProductCreate>();

    public List<IReadOnlyList<N11RestProductUpdate>> UpdatedBatches { get; } = new();
    public List<IReadOnlyList<N11RestPriceStock>> PriceStockBatches { get; } = new();

    public Task<IReadOnlyList<N11TaskSubmission>> CreateProductsAsync(
        IReadOnlyList<N11RestProductCreate> products, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        CreatedBatches.Add(products);
        return Task.FromResult<IReadOnlyList<N11TaskSubmission>>(
            new List<N11TaskSubmission> { new("TASK-1", "IN_QUEUE") });
    }

    public Task<IReadOnlyList<N11TaskSubmission>> UpdateProductsAsync(
        IReadOnlyList<N11RestProductUpdate> products, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        UpdatedBatches.Add(products);
        return Task.FromResult<IReadOnlyList<N11TaskSubmission>>(
            new List<N11TaskSubmission> { new("TASK-1", "IN_QUEUE") });
    }

    public Task<IReadOnlyList<N11TaskSubmission>> UpdatePriceStockAsync(
        IReadOnlyList<N11RestPriceStock> rows, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        PriceStockBatches.Add(rows);
        return Task.FromResult<IReadOnlyList<N11TaskSubmission>>(
            new List<N11TaskSubmission> { new("TASK-1", "IN_QUEUE") });
    }
}

/// <summary>
/// Task sorgulayÄ±cÄ±nÄ±n TEST sahtesi â€” varsayÄ±lan olarak "iÅŸlendi, tÃ¼m satÄ±rlar baÅŸarÄ±lÄ±" dÃ¶ner ki push akÄ±ÅŸÄ±
/// uÃ§tan uca koÅŸsun. Testler <see cref="Result"/>'Ä± deÄŸiÅŸtirip kuyrukta-kalma / red dallarÄ±nÄ± da sÄ±nayabilir.
/// </summary>
public sealed class FakeN11TaskPoller : IN11TaskPoller
{
    public N11TaskResult Result { get; set; } =
        new(N11TaskState.Processed, Array.Empty<N11TaskItemResult>(), null);

    public List<string> QueriedTaskIds { get; } = new();

    public Task<N11TaskResult> QueryAsync(string taskId, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        QueriedTaskIds.Add(taskId);
        return Task.FromResult(Result);
    }
}

/// <summary>
/// <c>GET /ms/product-query</c> TEST sahtesi â€” push sonrasÄ± geri okuma artÄ±k buradan yapÄ±lÄ±yor (REST yazma ucu
/// Ã¼rÃ¼n kimliÄŸini dÃ¶ndÃ¼rmediÄŸi iÃ§in N11ProductId'yi ancak bu okuma verir).
///
/// <para>VarsayÄ±lan: BOÅ sayfa â†’ "N11'de henÃ¼z gÃ¶rÃ¼nmÃ¼yor" (push'un hemen ardÄ±ndan gerÃ§ekÃ§i olan da budur;
/// akÄ±ÅŸ bu hÃ¢lde de saÄŸlÄ±klÄ± ilerlemeli). Testler <see cref="Page"/>'i doldurup kimlik/kategori dallarÄ±nÄ± sÄ±nar.</para>
/// </summary>
public sealed class FakeN11ProductQueryClient : IN11ProductQueryClient
{
    public N11RestProductPage Page { get; set; } =
        new(Array.Empty<N11RestProductSummary>(), 0, 0, 0L);

    public List<N11ProductQueryFilter> Queries { get; } = new();

    public Task<N11RestProductPage> QueryAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Queries.Add(filter);
        return Task.FromResult(Page);
    }

    public Task<IReadOnlyList<N11RestProductSummary>> QueryAllAsync(
        N11ProductQueryFilter filter, string appKey, string appSecret, CancellationToken cancellationToken = default)
    {
        Queries.Add(filter);
        return Task.FromResult(Page.Items);
    }
}
