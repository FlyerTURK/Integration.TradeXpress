using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.Timing;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SATIŞA DOĞRULAMANIN İNSAN YOLU — <c>Draft/Closed/Suspended → Ready</c>.
///
/// <para><b>Kapatılan açık:</b> push kapısı (<see cref="VariantSaleReadinessResolver"/>) fail-closed
/// ÇALIŞIYORDU, ama onayı verecek yol hiç yoktu: <c>ProductVariantDetail.MarkVerified</c> ve
/// <c>Product.MarkSaleReady</c>'nin üretim kodunda SIFIR çağıranı vardı. Sonuç canlıda 165/165 varyantın
/// <c>Draft</c> kalması ve hiçbir ürünün pazaryerine çıkamamasıydı. Hata sessizdi: kapı tam da tasarlandığı
/// gibi çalışıyor, yalnız kimse açamıyordu.</para>
///
/// <para><b>Damga TEK KAYNAKTAN</b> (<see cref="VariantSaleReadinessResolver.ComputeStampsAsync"/>): onay
/// anındaki reçete damgasını burada YAZIYORUZ, kapı sonradan OKUYOR. İkisi damgayı ayrı ayrı hesaplasaydı en
/// küçük formül farkı "onaylandı ama hiçbir zaman geçerli sayılmıyor" gibi mesajsız bir kilit üretirdi.</para>
///
/// <para><b>Kapı fail-closed KALIR</b> — burada eklenen yalnız insan yolu. Reçete sonradan değişirse damga
/// eskir ve onay KENDİLİĞİNDEN düşer; ayrı bir olay altyapısı bilinçli olarak yoktur.</para>
/// </summary>
public class ProductSaleVerifier : ITransientDependency
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantDetail, Guid> _detailRepository;
    private readonly VariantSaleReadinessResolver _readinessResolver;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentUser _currentUser;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IClock _clock;

    public ProductSaleVerifier(
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantDetail, Guid> detailRepository,
        VariantSaleReadinessResolver readinessResolver,
        ICurrentCompany currentCompany,
        ICurrentUser currentUser,
        IAsyncQueryableExecuter asyncExecuter,
        IClock clock)
    {
        _productRepository  = productRepository;
        _variantRepository  = variantRepository;
        _detailRepository   = detailRepository;
        _readinessResolver  = readinessResolver;
        _currentCompany     = currentCompany;
        _currentUser        = currentUser;
        _asyncExecuter      = asyncExecuter;
        _clock              = clock;
    }

    public async Task<ProductSaleVerifyResultDto> VerifyAsync(ProductSaleVerifyInputDto input)
    {
        var result = new ProductSaleVerifyResultDto();

        // Sahiplik: yabancı şirketin ürünü doğrulanamaz (açık koşul + global filtre — derinlemesine savunma).
        var product = await _productRepository.GetOwnedAsync(_currentCompany, input.ProductId);

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));

        var requested = input.VariantIds is { Count: > 0 }
            ? input.VariantIds.Distinct().ToHashSet()
            : null;

        // İstenip de bulunamayan varyant SESSİZ ATLANMAZ: kullanıcı "hepsini doğrula" deyip bazılarının
        // açılmadığını fark etmezse, ürünün neden hâlâ push edilemediğini asla bulamaz.
        if (requested is not null)
        {
            foreach (var missing in requested.Where(id => variants.All(v => v.Id != id)))
            {
                result.Issues.Add($"Varyant bu ürüne ait değil ya da bulunamadı: {missing}");
            }
        }

        var targets = variants
            .Where(v => requested is null || requested.Contains(v.Id))
            .ToList();

        // PASİF varyant doğrulanmaz — satışa kapalı olmasını kullanıcı zaten kendisi seçmiş.
        foreach (var passive in targets.Where(v => !v.IsActive))
        {
            result.Issues.Add($"Varyant pasif olduğu için doğrulanmadı: {passive.Code}");
        }

        var activeIds = targets.Where(v => v.IsActive).Select(v => v.Id).ToList();
        if (activeIds.Count == 0)
        {
            result.Issues.Add("Doğrulanacak aktif varyant bulunamadı.");
            return result;
        }

        var stamps = await _readinessResolver.ComputeStampsAsync(activeIds);
        var now = _clock.Now.ToUniversalTime();
        var verifiedBy = _currentUser.Id;

        var details = await _asyncExecuter.ToListAsync(
            (await _detailRepository.GetQueryableAsync())
                .Where(d => activeIds.Contains(d.EntityVariantId)));

        foreach (var variantId in activeIds)
        {
            var detail = details.FirstOrDefault(d => d.EntityVariantId == variantId);
            var stamp = stamps.GetValueOrDefault(variantId, RecipeVerificationStamp.EmptyRecipe);

            if (detail is null)
            {
                // Detay kaydı yoksa AÇILIR: varyantın satış statüsü orada yaşar, kaydı olmayan varyant
                // kapının gözünde "bilinmiyor" = satılamaz demektir.
                detail = new ProductVariantDetail(product.CompanyId, variantId);
                detail.MarkVerified(stamp, now, verifiedBy);
                await _detailRepository.InsertAsync(detail, autoSave: true);
            }
            else
            {
                detail.MarkVerified(stamp, now, verifiedBy);
                await _detailRepository.UpdateAsync(detail, autoSave: true);
            }

            result.VerifiedVariants++;
        }

        product.MarkSaleReady();
        await _productRepository.UpdateAsync(product, autoSave: true);
        result.ProductMarkedReady = true;

        return result;
    }
}
