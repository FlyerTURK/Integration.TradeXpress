using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Products;

/// <summary>
/// PUSH GUARD'I — "bu varyant pazaryerine çıkabilir mi?" sorusunun TEK yeri (2026-08-05 Hakan kararı).
///
/// <para>İki koşul birlikte aranır:
/// <list type="number">
///   <item>Varyantın statüsü <see cref="ProductSaleStatus.Ready"/> — yani bir İNSAN onaylamış.</item>
///   <item>Onay anındaki reçete stamp'i (<c>VerifiedRecipeStamp</c>) BUGÜNKÜ reçeteyle uyuşuyor — yani onaydan sonra reçete değişmemiş.</item>
/// </list>
/// İkincisi olmadan onay bir kereye mahsus tik olurdu: reçete sonradan değişir, ürün "onaylı" görünmeye
/// devam eder ve yanlış fiyatla satılır.</para>
///
/// <para><b>Neden merkezî servis, N11'in içinde değil:</b> aynı guard Trendyol ve Etsy push'larında da
/// gerekiyor. Kanal başına kopyalanan bir kural zamanla birbirinden ayrışır — ve ayrışma SESSİZ olur
/// (bir kanal doğrulamayı sorar, diğeri sormaz). §4: en merkezi, devralınabilir yerleşim.</para>
///
/// <para><b>Bu guard <c>OverridePrice</c> baypasını da kapatır:</b> push fiyat zinciri
/// <c>OverridePrice ?? türetilmiş</c> okuduğu için, elle fiyat girilmiş bir varyant reçetesi kararsız olsa
/// bile push edilebiliyordu. Guard fiyatlamadan ÖNCE olduğu için elle fiyat artık kararsızlığı örtmez.</para>
/// </summary>
public class VariantSaleReadinessResolver : ITransientDependency
{
    private readonly IRepository<ProductVariantDetail, Guid> _detailRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public VariantSaleReadinessResolver(
        IRepository<ProductVariantDetail, Guid> detailRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _detailRepository = detailRepository;
        _recipeLineRepository = recipeLineRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Verilen varyantlardan pazaryerine ÇIKABİLECEK olanların kimlikleri.
    /// <para>Fail-closed: detayı okunamayan, statüsü <c>Ready</c> olmayan ya da stamp'i tutmayan varyant
    /// kümeye GİRMEZ. "Bilinmiyor" asla "satılabilir" sayılmaz.</para></summary>
    public virtual async Task<HashSet<Guid>> ResolveSellableAsync(IReadOnlyCollection<Guid> variantIds)
    {
        ArgumentNullException.ThrowIfNull(variantIds);
        if (variantIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = variantIds.Distinct().ToList();

        var details = await _asyncExecuter.ToListAsync(
            (await _detailRepository.GetQueryableAsync())
                .Where(d => ids.Contains(d.EntityVariantId)));

        // Yalnız onaylı varyantların reçetesi okunur — kalanların stamp'ine zaten bakılmayacak.
        var readyIds = details
            .Where(d => d.SaleStatus == ProductSaleStatus.Ready)
            .Select(d => d.EntityVariantId)
            .ToList();

        if (readyIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var stampByVariant = await ComputeCurrentStampsAsync(readyIds);

        return details
            .Where(d => readyIds.Contains(d.EntityVariantId)
                        && d.IsVerificationCurrent(
                            stampByVariant.GetValueOrDefault(
                                d.EntityVariantId, RecipeVerificationStamp.EmptyRecipe)))
            .Select(d => d.EntityVariantId)
            .ToHashSet();
    }

    /// <summary>Varyant başına BUGÜNKÜ reçete stamp'i (<see cref="RecipeVerificationStamp"/>). Reçetesi olmayan
    /// varyant boş-reçete stamp'i alır — böylece "reçetesizken onaylandı, hâlâ reçetesiz" hâli geçerli kalır.
    ///
    /// <para><b>PUBLIC olması bilinçli:</b> insan doğrulama ucu (<c>VerifySaleReadinessAsync</c>) onay anındaki
    /// stamp'i YAZAR, bu resolver ise sonradan OKUR. İki taraf stamp'i ayrı ayrı hesaplasaydı en küçük formül
    /// farkı bile "onaylandı ama hiçbir zaman geçerli sayılmıyor" gibi sessiz bir kilide dönerdi — hata mesajı
    /// üretmeyen, yalnız ürünün push edilemediği bir hâl. Tek kaynak (§4 SSOT).</para></summary>
    public virtual async Task<Dictionary<Guid, string>> ComputeStampsAsync(List<Guid> variantIds)
    {
        return await ComputeCurrentStampsAsync(variantIds);
    }

    private async Task<Dictionary<Guid, string>> ComputeCurrentStampsAsync(List<Guid> variantIds)
    {
        var lines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.ProductVariantId)));

        var byVariant = lines.GroupBy(l => l.ProductVariantId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new Dictionary<Guid, string>();
        foreach (var variantId in variantIds)
        {
            var variantLines = byVariant.TryGetValue(variantId, out var found)
                ? found
                : new List<ProductVariantRecipeLine>();

            result[variantId] = RecipeVerificationStamp.Compute(variantLines.Select(ToStampLine));
        }

        return result;
    }

    /// <summary>Reçete satırı → <see cref="RecipeStampLine"/> girdisi. Zaman kısmı son değişim (yoksa oluşturulma) anıdır.</summary>
    private static RecipeStampLine ToStampLine(ProductVariantRecipeLine line)
    {
        return new RecipeStampLine(
            line.LineOrder,
            (int)line.ComponentType,
            line.CommodityProcessType is { } family ? (int)family : null,
            line.CommodityId,
            line.CommodityVariantId,
            line.Quantity,
            line.Amount,
            line.Factor,
            line.LastModificationTime ?? line.CreationTime);
    }
}
