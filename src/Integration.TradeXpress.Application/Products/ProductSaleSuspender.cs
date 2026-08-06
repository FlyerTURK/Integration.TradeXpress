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
/// KADEMELİ ASKIYA ALMA (2026-08-05 Hakan kararı) — bir şey bozulduğunda etkilenen varyantları, gerekiyorsa
/// ürünü satıştan çeker.
///
/// <para><b>Kademe:</b> önce VARYANT askıya alınır; ürün ancak <b>TÜM</b> varyantları düştüğünde askıya
/// alınır. 3 varyantlı üründe biri etkilenirse diğer ikisi satışta kalır — sağlam varyantların satışını
/// durdurmak gereksiz zarardır.</para>
///
/// <para><b>Tek yön:</b> burada yalnız <c>Ready → Suspended</c> vardır. Ters yön (geri açma) BİLEREK yoktur —
/// geri dönüş yalnız insandan, doğrulama akışından geçer. Aksi halde bozulan şey düzelince ürün kendiliğinden
/// satışa döner ve kimse reçeteye bakmamış olur.</para>
///
/// <para><b>Neden ayrı servis:</b> aynı kademeli mantık ileride başka tetiklerden de çağrılacak (emtia
/// pasifleşmesi bugün; reçete bozulması, fiyat çözülememesi yarın). Çağrı yerine kopyalanan bir kademe
/// kuralı zamanla ayrışır ve ayrışma SESSİZ olur.</para>
/// </summary>
public class ProductSaleSuspender : ITransientDependency
{
    private const string ProductEntityName = "Product";

    private readonly IRepository<ProductVariantDetail, Guid> _detailRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public ProductSaleSuspender(
        IRepository<ProductVariantDetail, Guid> detailRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<Product, Guid> productRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _detailRepository = detailRepository;
        _variantRepository = variantRepository;
        _productRepository = productRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Verilen varyantları askıya alır; sahibi ürünün tüm varyantları düştüyse ürünü de.</summary>
    public virtual async Task SuspendVariantsAsync(IReadOnlyCollection<Guid> variantIds)
    {
        ArgumentNullException.ThrowIfNull(variantIds);
        if (variantIds.Count == 0)
        {
            return;
        }

        var ids = variantIds.Distinct().ToList();

        var details = await _asyncExecuter.ToListAsync(
            (await _detailRepository.GetQueryableAsync())
                .Where(d => ids.Contains(d.EntityVariantId)));

        var suspended = new List<Guid>();
        foreach (var detail in details)
        {
            // Suspend() yalnız Ready'yi düşürür — Draft/Closed'a DOKUNMAZ (idempotent + kullanıcı kararına saygı).
            if (detail.SaleStatus != ProductSaleStatus.Ready)
            {
                continue;
            }

            detail.Suspend();
            await _detailRepository.UpdateAsync(detail, autoSave: true);
            suspended.Add(detail.EntityVariantId);
        }

        if (suspended.Count == 0)
        {
            return;
        }

        await SuspendFullyAffectedProductsAsync(suspended);
    }

    /// <summary>Askıya alınan varyantların ürünlerini bulur; ürünün BAŞKA satılabilir varyantı kalmadıysa
    /// ürünü de askıya alır.</summary>
    private async Task SuspendFullyAffectedProductsAsync(List<Guid> suspendedVariantIds)
    {
        var owners = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && suspendedVariantIds.Contains(v.Id))
                .Select(v => v.EntityId)
                .Distinct());

        foreach (var productId in owners)
        {
            // Ürünün TÜM (aktif) varyantları — biri hâlâ Ready ise ürün satışta kalır.
            var siblingIds = await _asyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && v.EntityId == productId && v.IsActive)
                    .Select(v => v.Id));

            if (siblingIds.Count == 0)
            {
                continue;
            }

            var anyStillReady = await _asyncExecuter.AnyAsync(
                (await _detailRepository.GetQueryableAsync())
                    .Where(d => siblingIds.Contains(d.EntityVariantId)
                                && d.SaleStatus == ProductSaleStatus.Ready));

            if (anyStillReady)
            {
                continue;
            }

            var product = await _productRepository.FindAsync(productId);
            if (product is null)
            {
                continue;
            }

            product.SuspendSale();
            await _productRepository.UpdateAsync(product, autoSave: true);
        }
    }
}
