using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// TERS-ENDEKS: maden → ürün (ADR-PRODUCT-ORCHESTRATION). "Bu maden(ler)i reçetesinde taşıyan ürünler hangileri?"
/// — maden stoğu değişince (VoucherLine tetiği) yeniden hesaplanacak ürün kümesini bulur.
/// <para><b>İki hop:</b> reçete satırı yalnız <c>ProductVariantId</c> (EntityVariant.Id) verir →
/// <c>EntityVariant(EntityName="Product")</c> üzerinden <c>EntityId</c> = ProductId çözülür.</para>
/// <para><b>Varyant granülerliği:</b> hem değişen metal varyantına bağlı satırlar (<c>CommodityVariantId</c> eşleşen)
/// hem varyantsız satırlar (<c>CommodityVariantId == null</c> — "ana varyant" anlamına gelir) yakalanır;
/// aksi halde eksik eşleşme oversell kapısını açık bırakırdı.</para>
/// <para><b>Aile filtresi zorunlu:</b> CommodityId FK'sız snapshot — aynı Guid farklı ailede (Scrap/Jewelry)
/// çakışabilir; <c>CommodityProcessType == Metal</c> + <c>ComponentType == CatalogCommodity</c> filtreleri atlanamaz.</para>
/// <para><b>Kapsam:</b> sorgular ABP company/tenant filtreleriyle daralır (ProductVariantRecipeLine ICompanyOwned);
/// çağıran doğru company bağlamını kurmakla yükümlüdür (job'larda elle kurulur — GetStockAsync sözleşmesiyle aynı).</para>
/// <para>Kompozit indeks: (TenantId, CompanyId, CommodityProcessType, CommodityId, CommodityVariantId) —
/// RecipeMetalReverseIndex migration'ı. İndekssiz bu sorgu company içi tam taramaydı.</para>
/// </summary>
public class RecipeMetalReverseIndex : ITransientDependency
{
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _groupItemRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    private const string ProductEntityName = "Product";

    public RecipeMetalReverseIndex(
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<Product, Guid> productRepository,
        IRepository<SubstitutionGroupItem, Guid> groupItemRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _recipeLineRepository = recipeLineRepository;
        _variantRepository = variantRepository;
        _productRepository = productRepository;
        _groupItemRepository = groupItemRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Verilen madenleri reçetesinde taşıyan ÜRÜNLERİ döner (ürün-başına etkilenen varyant id'leriyle).
    /// İki kaynak birleşir:
    /// <list type="number">
    ///   <item>REÇETE yolu — satırı bu maden(ler)e işaret eden varyantlar (muadil OLMAYAN ürünler dahil).</item>
    ///   <item>MUADİL-GRUP yolu — <c>Product.SubstitutionGroupId</c> bu madenleri İÇEREN gruba işaret eden ürünler:
    ///   muadil ürünün henüz reçetesine girmemiş bir grup madeni de stok değişince kombinasyonları değiştirir
    ///   (yeni kombinasyon DOĞABİLİR) — yalnız reçete yolu bunu kaçırırdı.</item>
    /// </list></summary>
    public virtual async Task<IReadOnlyList<AffectedProduct>> FindAffectedProductsAsync(
        IReadOnlyCollection<MetalStockKey> changedMetals)
    {
        if (changedMetals.Count == 0)
        {
            return Array.Empty<AffectedProduct>();
        }

        var metalIds = changedMetals.Select(m => m.MetalId).Distinct().ToList();

        // Maden-BAŞINA değişen varyant kümesi (2026-07-25 inceleme bulgusu #4): global düzleştirilmiş küme,
        // karma event'te ((A,varyantlı) + (B,varyantsız)) A'nın varyantsız-değişimini B'nin varyant filtresine
        // taktırıp EKSİK eşleşme (→ oversell) üretiyordu. (metal, null) anahtarı = o madenin TÜM satırları.
        var changedByMetal = changedMetals
            .GroupBy(m => m.MetalId)
            .ToDictionary(
                g => g.Key,
                g => g.Any(k => k.MetalVariantId is null)
                    ? null   // varyantsız değişim var → o madenin her satırı etkilenir
                    : g.Select(k => k.MetalVariantId!.Value).ToHashSet());

        // ── 1. hop: SQL yalnız METAL filtresi (indeksli); varyant granülerliği BELLEKTE maden-başına —
        //    satır sayısı maden-başına küçük, doğruluk sorgu karmaşıklığına feda edilmez.
        var candidateLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(r => r.ComponentType == RecipeComponentType.CatalogCommodity
                            && r.CommodityProcessType == ProcessType.Metal
                            && r.CommodityId != null
                            && metalIds.Contains(r.CommodityId!.Value))
                .Select(r => new { r.ProductVariantId, r.CommodityId, r.CommodityVariantId }));

        var affectedVariantIds = candidateLines
            .Where(l =>
            {
                var variantSet = changedByMetal[l.CommodityId!.Value];
                // Satır varyantsızsa her değişim etkiler; maden varyantsız değiştiyse (null küme) her satır etkilenir.
                return l.CommodityVariantId is null
                       || variantSet is null
                       || variantSet.Contains(l.CommodityVariantId.Value);
            })
            .Select(l => l.ProductVariantId)
            .Distinct()
            .ToList();

        // ── 2. hop: varyant → ürün (EntityVariant agnostik; yalnız Product sahipli olanlar).
        var variantPairs = affectedVariantIds.Count == 0
            ? new List<VariantOwner>()
            : await _asyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == ProductEntityName && affectedVariantIds.Contains(v.Id))
                    .Select(v => new VariantOwner(v.EntityId, v.Id)));

        var variantsByProduct = variantPairs
            .GroupBy(p => p.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.VariantId).ToList());

        // ── Muadil-grup yolu: grubu bu madenleri İÇEREN muadil ürünler. Reçete yolu tek başına YETMEZ:
        //    muadil ürünün mevcut reçetesinde olmayan bir grup madeninin stoğu artınca YENİ kombinasyon
        //    doğabilir (ör. G2.5 stoğu gelince 2.5×2+1×3 aday olur) — o ürün de yeniden hesaplanmalı.
        var groupsContainingMetals = (await _groupItemRepository.GetQueryableAsync())
            .Where(i => i.MetalId != null && metalIds.Contains(i.MetalId!.Value))
            .Select(i => i.SubstitutionGroupId)
            .Distinct();
        var groupProductIds = await _asyncExecuter.ToListAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => p.VariantMode == ProductVariantMode.Substitution
                            && p.SubstitutionGroupId != null
                            && groupsContainingMetals.Contains(p.SubstitutionGroupId.Value))
                .Select(p => p.Id));

        var results = new List<AffectedProduct>();
        var productIds = variantsByProduct.Keys.Union(groupProductIds).Distinct().ToList();
        if (productIds.Count == 0)
        {
            return results;
        }

        var products = await _asyncExecuter.ToListAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.VariantMode, p.StockPolicy, p.SubstitutionGroupId }));

        foreach (var p in products)
        {
            // Fixed/Unlimited politika: orkestratör DOKUNMAZ (Hakan kararı — elle stok ezilmez, o Fixed'in kendisi).
            if (p.StockPolicy != ProductStockPolicy.Calculated)
            {
                continue;
            }

            // Muadil-grup yolundan gelen ama reçete yolu boş olan ürün: grup üyeliği orkestratörde doğrulanır.
            results.Add(new AffectedProduct(
                p.Id,
                p.VariantMode == ProductVariantMode.Substitution,
                p.SubstitutionGroupId,
                variantsByProduct.TryGetValue(p.Id, out var variants) ? variants : new List<Guid>()));
        }

        return results;
    }
}

/// <summary>Değişen maden anahtarı — VoucherLine'dan (CommodityId + opsiyonel varyant).</summary>
public readonly record struct MetalStockKey(Guid MetalId, Guid? MetalVariantId);

/// <summary>Etkilenen ürün: muadil mi (yeniden üretim) değil mi (satılabilir adet), hangi varyantları değişti.</summary>
public sealed record AffectedProduct(
    Guid ProductId,
    bool IsSubstitution,
    Guid? SubstitutionGroupId,
    List<Guid> AffectedVariantIds);

file readonly record struct VariantOwner(Guid ProductId, Guid VariantId);
