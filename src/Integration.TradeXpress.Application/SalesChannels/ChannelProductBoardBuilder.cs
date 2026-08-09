using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>Kanal tahtasının KANAL-AGNOSTİK satır verisi — ürün kimliği, görseli ve "karar bekliyor mu" sinyali.</summary>
public sealed record ChannelProductBoardRow(
    string ProductCode,
    string ProductName,
    string? ImageUrl,
    int VariantCount,
    bool HasRecipe,
    int ReadyVariantCount);

/// <summary>
/// KANAL TAHTALARININ ORTAK GÖVDESİ — "bu üründe daha ne yapılacak?" sorusunun tek cevap yeri.
///
/// <para><b>Neden ortak sınıf:</b> N11 ve Trendyol tahtalarının kanal-özel kısmı birkaç kolondan ibaret
/// (Trendyol'da pazaryeri fiyatı/adedi, N11'de satış/onay durumu), ama <b>karar sinyali</b> — reçete var mı,
/// kaç varyant satışa hazır — ikisinde de HARFİ HARFİNE aynı. Bu ~80 satırlık sorguyu ikinci kanala
/// kopyalamak connascence-of-algorithm üretirdi: satılabilirlik kuralı değişince biri güncellenir, diğeri
/// sessizce eski kalırdı — ve fark ancak "bu ürün neden push edilmiyor?" diye sorulduğunda görülürdü.</para>
///
/// <para><b>Reçete VARLIĞI ölçülür, içeriği değil:</b> tahta "sınıflandırıldı mı" sorusuna cevap verir;
/// maliyet hesabı push zincirinin işidir. Tek satır bile varsa ürün "reçetesiz" listesinden çıkar.</para>
///
/// <para><b>Satılabilirlik kapının BUGÜNKÜ kararıdır:</b> <c>Ready</c> olmak yetmez, doğrulama damgasının
/// tazeliği de aranır (<see cref="VariantSaleReadinessResolver"/>). Yani tahta "onaylanmış" değil "şu an
/// geçer" sayısını gösterir — kullanıcı reçeteyi değiştirdiyse sayı düşer ve sebebi budur.</para>
///
/// <para><b>N+1 YOK:</b> ürün · varyant · reçete · görsel dört TOPLU sorguyla çekilir. Ürün başına çözücü
/// çalıştırmak 103 kayıtta 400+ sorgu demekti.</para>
/// </summary>
public class ChannelProductBoardBuilder : ITransientDependency
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly VariantSaleReadinessResolver _saleReadiness;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ChannelProductBoardBuilder(
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        VariantSaleReadinessResolver saleReadiness,
        IEntityMediaAppService entityMedia,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _recipeLineRepository = recipeLineRepository;
        _saleReadiness = saleReadiness;
        _entityMedia = entityMedia;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Ürün başına ortak tahta satırı. Ürünü bulunamayan kimlik sözlüğe GİRMEZ — çağıran o kanal
    /// kaydını atlamak yerine boş alanlarla göstermeyi seçebilir (karar onundur).</summary>
    public virtual async Task<Dictionary<Guid, ChannelProductBoardRow>> BuildAsync(IReadOnlyCollection<Guid> productIds)
    {
        var result = new Dictionary<Guid, ChannelProductBoardRow>();
        if (productIds.Count == 0)
        {
            return result;
        }

        var ids = productIds.Distinct().ToList();

        var products = (await _asyncExecuter.ToListAsync(
                (await _productRepository.GetQueryableAsync()).Where(p => ids.Contains(p.Id))))
            .ToDictionary(p => p.Id);

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && ids.Contains(v.EntityId) && v.IsActive));

        var variantIds = variants.Select(v => v.Id).ToList();

        var recipeVariantIds = (await _asyncExecuter.ToListAsync(
                (await _recipeLineRepository.GetQueryableAsync())
                    .Where(l => variantIds.Contains(l.ProductVariantId))
                    .Select(l => l.ProductVariantId)))
            .ToHashSet();

        var sellable = await _saleReadiness.ResolveSellableAsync(variantIds);
        var posterByProduct = await _entityMedia.GetDefaultPosterMapAsync(ProductEntityName, ids);
        var variantsByProduct = variants.GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var id in ids)
        {
            if (!products.TryGetValue(id, out var product))
            {
                continue;
            }

            var productVariants = variantsByProduct.GetValueOrDefault(id) ?? new List<EntityVariant>();

            result[id] = new ChannelProductBoardRow(
                ProductCode: product.Code,
                ProductName: product.Name,
                ImageUrl: posterByProduct.GetValueOrDefault(id),
                VariantCount: productVariants.Count,
                HasRecipe: productVariants.Any(v => recipeVariantIds.Contains(v.Id)),
                ReadyVariantCount: productVariants.Count(v => sellable.Contains(v.Id)));
        }

        return result;
    }
}
