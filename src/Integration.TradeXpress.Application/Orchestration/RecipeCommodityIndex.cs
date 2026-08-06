using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.N11Products;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.RecipeTemplates;
using Integration.TradeXpress.Substitutions;
using Integration.TradeXpress.TrendyolProducts;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Orchestration;

/// <summary>
/// TERS-ENDEKS: emtia → ürün. İKİ ROLÜ vardır ve kapsamları BİLEREK farklıdır:
/// <list type="number">
///   <item><see cref="FindAffectedProductsAsync"/> — STOK yeniden-hesabı (ADR-PRODUCT-ORCHESTRATION):
///   emtia stoğu değişince (VoucherLine tetiği) yeniden hesaplanacak ürünler. <c>Calculated</c> ile daraltır,
///   muadil-grup yolunu da katar. Kapsam = <see cref="CommodityStockFamilies.Tracked"/> (bugün Metal + Good) —
///   tetiği yayan taraf ile AYNI listeye bakar.</item>
///   <item><see cref="FindUsageAsync"/> — KULLANIM sorgusu (emtia silme/pasifleştirme guard'ı): TÜM aileler,
///   stok politikası filtresi YOK.</item>
/// </list>
/// <para><b>İki hop:</b> reçete satırı yalnız <c>ProductVariantId</c> (EntityVariant.Id) verir →
/// <c>EntityVariant(EntityName="Product")</c> üzerinden <c>EntityId</c> = ProductId çözülür.</para>
/// <para><b>Varyant granülerliği:</b> hem değişen metal varyantına bağlı satırlar (<c>CommodityVariantId</c> eşleşen)
/// hem varyantsız satırlar (<c>CommodityVariantId == null</c> — "ana varyant" anlamına gelir) yakalanır;
/// aksi halde eksik eşleşme oversell kapısını açık bırakırdı.</para>
/// <para><b>Aile filtresi zorunlu:</b> CommodityId FK'sız snapshot — aynı Guid farklı ailede (Scrap/Jewelry)
/// çakışabilir; <c>CommodityProcessType</c> + <c>ComponentType == CatalogCommodity</c> filtreleri atlanamaz.</para>
/// <para><b>Kapsam:</b> sorgular ABP company/tenant filtreleriyle daralır (ProductVariantRecipeLine ICompanyOwned);
/// çağıran doğru company bağlamını kurmakla yükümlüdür (job'larda elle kurulur — GetStockAsync sözleşmesiyle aynı).</para>
/// <para>Kompozit indeks: (TenantId, CompanyId, CommodityProcessType, CommodityId, CommodityVariantId) —
/// RecipeMetalReverseIndex migration'ı. İndekssiz bu sorgu company içi tam taramaydı.</para>
/// </summary>
public class RecipeCommodityIndex : ITransientDependency
{
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<SubstitutionGroupItem, Guid> _groupItemRepository;
    private readonly IRepository<RecipeTemplateLine, Guid> _templateLineRepository;
    private readonly IRepository<RecipeTemplate, Guid> _templateRepository;
    private readonly IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> _n11RecipeLineRepository;
    private readonly IRepository<SalesChannelTrN11Product, Guid> _n11ProductRepository;
    private readonly IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> _trendyolRecipeLineRepository;
    private readonly IRepository<SalesChannelTrTrendyolProduct, Guid> _trendyolProductRepository;
    private readonly IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> _etsyRecipeLineRepository;
    private readonly IRepository<SalesChannelEtsyProduct, Guid> _etsyProductRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    private const string ProductEntityName = "Product";

    public RecipeCommodityIndex(
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<Product, Guid> productRepository,
        IRepository<SubstitutionGroupItem, Guid> groupItemRepository,
        IRepository<RecipeTemplateLine, Guid> templateLineRepository,
        IRepository<RecipeTemplate, Guid> templateRepository,
        IRepository<SalesChannelTrN11ProductStockItemRecipeLine, Guid> n11RecipeLineRepository,
        IRepository<SalesChannelTrN11Product, Guid> n11ProductRepository,
        IRepository<SalesChannelTrTrendyolProductStockItemRecipeLine, Guid> trendyolRecipeLineRepository,
        IRepository<SalesChannelTrTrendyolProduct, Guid> trendyolProductRepository,
        IRepository<SalesChannelEtsyProductStockItemRecipeLine, Guid> etsyRecipeLineRepository,
        IRepository<SalesChannelEtsyProduct, Guid> etsyProductRepository,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _recipeLineRepository = recipeLineRepository;
        _variantRepository = variantRepository;
        _productRepository = productRepository;
        _groupItemRepository = groupItemRepository;
        _templateLineRepository = templateLineRepository;
        _templateRepository = templateRepository;
        _n11RecipeLineRepository = n11RecipeLineRepository;
        _n11ProductRepository = n11ProductRepository;
        _trendyolRecipeLineRepository = trendyolRecipeLineRepository;
        _trendyolProductRepository = trendyolProductRepository;
        _etsyRecipeLineRepository = etsyRecipeLineRepository;
        _etsyProductRepository = etsyProductRepository;
        _asyncExecuter = asyncExecuter;
    }

    /// <summary>Verilen emtiaları reçetesinde taşıyan ÜRÜNLERİ döner (ürün-başına etkilenen varyant id'leriyle).
    /// İki kaynak birleşir:
    /// <list type="number">
    ///   <item>REÇETE yolu — satırı bu emtia(lar)a işaret eden varyantlar (muadil OLMAYAN ürünler dahil).</item>
    ///   <item>MUADİL-GRUP yolu — <c>Product.SubstitutionGroupId</c> bu madenleri İÇEREN gruba işaret eden ürünler:
    ///   muadil ürünün henüz reçetesine girmemiş bir grup madeni de stok değişince kombinasyonları değiştirir
    ///   (yeni kombinasyon DOĞABİLİR) — yalnız reçete yolu bunu kaçırırdı. <b>Bu yol Metal-only kalır:</b>
    ///   <c>SubstitutionGroupItem.MetalId</c> tanım gereği madene bağlıdır.</item>
    /// </list></summary>
    public virtual async Task<IReadOnlyList<AffectedProduct>> FindAffectedProductsAsync(
        IReadOnlyCollection<CommodityStockKey> changedCommodities)
    {
        if (changedCommodities.Count == 0)
        {
            return Array.Empty<AffectedProduct>();
        }

        var families = changedCommodities.Select(k => k.Family).Distinct().ToList();
        var commodityIds = changedCommodities.Select(k => k.CommodityId).Distinct().ToList();

        // EMTİA-BAŞINA değişen varyant kümesi (2026-07-25 inceleme bulgusu #4): global düzleştirilmiş küme,
        // karma event'te ((A,varyantlı) + (B,varyantsız)) A'nın varyantsız-değişimini B'nin varyant filtresine
        // taktırıp EKSİK eşleşme (→ oversell) üretiyordu. (emtia, null) anahtarı = o emtianın TÜM satırları.
        // Anahtar AİLEYİ de taşır: CommodityId FK'sız snapshot, aynı Guid başka ailede başka emtiadır.
        var changedByCommodity = changedCommodities
            .GroupBy(k => (k.Family, k.CommodityId))
            .ToDictionary(
                g => g.Key,
                g => g.Any(k => k.CommodityVariantId is null)
                    ? null   // varyantsız değişim var → o emtianın her satırı etkilenir
                    : g.Select(k => k.CommodityVariantId!.Value).ToHashSet());

        // ── 1. hop: SQL yalnız (aile, emtia) filtresi (indeksli); varyant granülerliği BELLEKTE emtia-başına —
        //    satır sayısı emtia-başına küçük, doğruluk sorgu karmaşıklığına feda edilmez.
        var candidateLines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(r => r.ComponentType == RecipeComponentType.CatalogCommodity
                            && r.CommodityProcessType != null
                            && families.Contains(r.CommodityProcessType!.Value)
                            && r.CommodityId != null
                            && commodityIds.Contains(r.CommodityId!.Value))
                .Select(r => new
                {
                    r.ProductVariantId, r.CommodityProcessType, r.CommodityId, r.CommodityVariantId,
                }));

        var affectedVariantIds = candidateLines
            .Where(l =>
            {
                // Aile × emtia ÇAPRAZI: SQL iki listeyi bağımsız filtreledi (aileA×emtiaB kombinasyonu da
                // döndü). Sözlükte anahtarı olmayan satır gerçekten değişmemiştir — elenir.
                if (!changedByCommodity.TryGetValue(
                        (l.CommodityProcessType!.Value, l.CommodityId!.Value), out var variantSet))
                {
                    return false;
                }

                // Satır varyantsızsa her değişim etkiler; emtia varyantsız değiştiyse (null küme) her satır etkilenir.
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
        //    METAL-ONLY (bilinçli): SubstitutionGroupItem tanım gereği madene bağlıdır (MetalId). Good
        //    değişimini bu dala sokmak, aynı Guid'i madenmiş gibi aratıp alakasız ürün uyandırırdı.
        var metalIds = changedCommodities
            .Where(k => k.Family == ProcessType.Metal)
            .Select(k => k.CommodityId)
            .Distinct()
            .ToList();
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

    /// <summary>
    /// KULLANIM SORGUSU (2026-08-05 Hakan kararı): "bu emtia hangi ürün varyantlarında kullanılıyor?" —
    /// emtia SİLİNİRKEN ya da PASİFLEŞTİRİLİRKEN kullanıcıyı uyarmak ve devam edilirse etkilenen ürünleri
    /// satışa kapatmak için.
    ///
    /// <para><b><see cref="FindAffectedProductsAsync"/>'ten neden AYRI:</b> o metot STOK yeniden-hesabı içindir
    /// ve <c>StockPolicy != Calculated</c> ürünleri ELER (Fixed'in stoğuna orkestratör dokunmaz — Hakan kararı).
    /// Burada eleme YANLIŞ olurdu: Fixed stoklu bir ürün de reçetesinde silinmiş bir mücevheri taşıyorsa
    /// satılmamalıdır. Aynı sorgu, farklı kapsam — bu yüzden tek metoda parametre eklemek yerine ayrı metot.</para>
    ///
    /// <para><b>Aile parametresi ZORUNLU:</b> <c>CommodityId</c> FK'sız snapshot; aynı Guid farklı ailede
    /// çakışabilir. Filtre <c>(ComponentType, CommodityProcessType, CommodityId)</c> kompozit indeksine oturur —
    /// migration gerekmez, indeks zaten aile kolonunu taşıyor.</para>
    ///
    /// <para>Varyant granülerliği YOK (bilinçli): emtianın HERHANGİ bir varyantı silinse/pasifleşse bile o
    /// emtiaya bağlı tüm satırlar riskli sayılır — uyarı tarafında dar davranmak sessiz kaçak üretir.</para>
    ///
    /// <para><b>BEŞ KAYNAK taranır</b> (2026-08-05 Hakan kararı): ürün reçetesi · reçete şablonu ·
    /// N11/Trendyol/Etsy kanal reçeteleri. Yalnız ürün reçetesine bakmak sert bloku DELERDİ — şablondaki ya da
    /// kanaldaki kullanım fark edilmeden emtia silinir, o reçete sessizce bozulurdu. Sonuç
    /// <see cref="CommodityUsage.Kind"/> ile ayrıştırılır: kullanıcıya "nereyi temizleyeceğini" söylemek
    /// gerekiyor, salt "kullanımda" mesajı çıkmaz sokaktır.</para>
    ///
    /// <para><b>⚠ Service ailesi FARKLI kolonlarda yaşar:</b> hizmet satırı <c>SetService</c> ile yazılır ve
    /// <c>CommodityProcessType</c>'ı <b>null</b> bırakır, <c>ComponentType</c> ise <c>Service</c>'tir. Katalog
    /// filtresinin iki kolonu da tutmaz → ayrı dal şart; yoksa Service emtiası "hiç kullanılmıyor" görünürdü.</para>
    /// </summary>
    public virtual async Task<IReadOnlyList<CommodityUsage>> FindUsageAsync(
        ProcessType family, IReadOnlyCollection<Guid> commodityIds)
    {
        ArgumentNullException.ThrowIfNull(commodityIds);
        if (commodityIds.Count == 0)
        {
            return Array.Empty<CommodityUsage>();
        }

        var ids = commodityIds.Distinct().ToList();
        var isService = family == ProcessType.Service;

        var results = new List<CommodityUsage>();
        results.AddRange(await FindProductRecipeUsageAsync(family, ids, isService));
        results.AddRange(await FindTemplateUsageAsync(family, ids, isService));
        results.AddRange(await FindChannelUsageAsync(family, ids, isService));
        return results;
    }

    // ── kaynak 1: ürün reçetesi (CANLI — silmeyi bloklar) ───────────────────────────────────────────

    private async Task<List<CommodityUsage>> FindProductRecipeUsageAsync(
        ProcessType family, List<Guid> ids, bool isService)
    {
        var query = (await _recipeLineRepository.GetQueryableAsync())
            .Where(r => r.CommodityId != null && ids.Contains(r.CommodityId!.Value));

        query = isService
            ? query.Where(r => r.ComponentType == RecipeComponentType.Service)
            : query.Where(r => r.ComponentType == RecipeComponentType.CatalogCommodity
                               && r.CommodityProcessType == family);

        var variantIds = await _asyncExecuter.ToListAsync(query.Select(r => r.ProductVariantId).Distinct());
        if (variantIds.Count == 0)
        {
            return new List<CommodityUsage>();
        }

        var owners = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && variantIds.Contains(v.Id))
                .Select(v => new VariantOwner(v.EntityId, v.Id)));

        var byProduct = owners
            .GroupBy(o => o.ProductId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.VariantId).ToList());

        var productIds = byProduct.Keys.ToList();
        var products = await _asyncExecuter.ToListAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Code, p.Name }));

        // Ürün kaydı okunamayan varyant DÜŞMEZ, kod/ad boş geçer: uyarı listesinden sessizce eksilmek,
        // kullanıcıya "kullanılmıyor" izlenimi verirdi.
        return byProduct
            .Select(kv =>
            {
                var p = products.FirstOrDefault(x => x.Id == kv.Key);
                return new CommodityUsage(
                    CommodityUsageKind.ProductRecipe,
                    kv.Key, p?.Code ?? string.Empty, p?.Name ?? string.Empty, kv.Value);
            })
            .ToList();
    }

    // ── kaynak 2: reçete şablonu (TASLAK — yalnız uyarı, silmeyi BLOKLAMAZ) ─────────────────────────

    private async Task<List<CommodityUsage>> FindTemplateUsageAsync(
        ProcessType family, List<Guid> ids, bool isService)
    {
        var query = (await _templateLineRepository.GetQueryableAsync())
            .Where(l => l.CommodityId != null && ids.Contains(l.CommodityId!.Value));

        query = isService
            ? query.Where(l => l.ComponentType == RecipeComponentType.Service)
            : query.Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                               && l.CommodityProcessType == family);

        var templateIds = await _asyncExecuter.ToListAsync(query.Select(l => l.TemplateId).Distinct());
        if (templateIds.Count == 0)
        {
            return new List<CommodityUsage>();
        }

        var templates = await _asyncExecuter.ToListAsync(
            (await _templateRepository.GetQueryableAsync())
                .Where(t => templateIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Name }));

        return templateIds
            .Select(id =>
            {
                var t = templates.FirstOrDefault(x => x.Id == id);
                return new CommodityUsage(
                    CommodityUsageKind.RecipeTemplate,
                    id, string.Empty, t?.Name ?? string.Empty, Array.Empty<Guid>());
            })
            .ToList();
    }

    // ── kaynak 3-5: kanal reçeteleri (CANLI — silmeyi bloklar) ──────────────────────────────────────

    private async Task<List<CommodityUsage>> FindChannelUsageAsync(
        ProcessType family, List<Guid> ids, bool isService)
    {
        var result = new List<CommodityUsage>();

        var n11Query = (await _n11RecipeLineRepository.GetQueryableAsync())
            .Where(l => l.CommodityId != null && ids.Contains(l.CommodityId!.Value));
        n11Query = isService
            ? n11Query.Where(l => l.ComponentType == RecipeComponentType.Service)
            : n11Query.Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                                  && l.CommodityProcessType == family);
        var n11Ids = await _asyncExecuter.ToListAsync(
            n11Query.Select(l => l.SalesChannelTrN11ProductId).Distinct());
        if (n11Ids.Count > 0)
        {
            var rows = await _asyncExecuter.ToListAsync(
                (await _n11ProductRepository.GetQueryableAsync())
                    .Where(p => n11Ids.Contains(p.Id))
                    .Select(p => new { p.Id, Code = p.SellerCode }));
            result.AddRange(ToChannelUsage(n11Ids, rows.ToDictionary(x => x.Id, x => x.Code), "N11"));
        }

        var tyQuery = (await _trendyolRecipeLineRepository.GetQueryableAsync())
            .Where(l => l.CommodityId != null && ids.Contains(l.CommodityId!.Value));
        tyQuery = isService
            ? tyQuery.Where(l => l.ComponentType == RecipeComponentType.Service)
            : tyQuery.Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                                 && l.CommodityProcessType == family);
        var tyIds = await _asyncExecuter.ToListAsync(
            tyQuery.Select(l => l.SalesChannelTrTrendyolProductId).Distinct());
        if (tyIds.Count > 0)
        {
            var rows = await _asyncExecuter.ToListAsync(
                (await _trendyolProductRepository.GetQueryableAsync())
                    .Where(p => tyIds.Contains(p.Id))
                    .Select(p => new { p.Id, Code = p.ProductMainId }));
            result.AddRange(ToChannelUsage(tyIds, rows.ToDictionary(x => x.Id, x => x.Code), "Trendyol"));
        }

        var etsyQuery = (await _etsyRecipeLineRepository.GetQueryableAsync())
            .Where(l => l.CommodityId != null && ids.Contains(l.CommodityId!.Value));
        etsyQuery = isService
            ? etsyQuery.Where(l => l.ComponentType == RecipeComponentType.Service)
            : etsyQuery.Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                                   && l.CommodityProcessType == family);
        var etsyIds = await _asyncExecuter.ToListAsync(
            etsyQuery.Select(l => l.SalesChannelEtsyProductId).Distinct());
        if (etsyIds.Count > 0)
        {
            var rows = await _asyncExecuter.ToListAsync(
                (await _etsyProductRepository.GetQueryableAsync())
                    .Where(p => etsyIds.Contains(p.Id))
                    .Select(p => new { p.Id, Code = p.SellerSkuBase }));
            result.AddRange(ToChannelUsage(etsyIds, rows.ToDictionary(x => x.Id, x => x.Code), "Etsy"));
        }

        return result;
    }

    // Kanal kaydı okunamayan satır DÜŞMEZ (ürün tarafındaki kuralla aynı): kod boş geçer ama uyarıda kalır.
    private static IEnumerable<CommodityUsage> ToChannelUsage(
        List<Guid> channelProductIds, Dictionary<Guid, string> codeById, string channelName)
    {
        return channelProductIds.Select(id => new CommodityUsage(
            CommodityUsageKind.ChannelRecipe,
            id,
            codeById.GetValueOrDefault(id) ?? string.Empty,
            channelName,
            Array.Empty<Guid>()));
    }
}

/// <summary>Kullanımın NEREDE olduğu. Silme davranışını belirler (2026-08-05 Hakan kararı).</summary>
public enum CommodityUsageKind : byte
{
    /// <summary>Ürün reçetesi — CANLI kullanım; silmeyi BLOKLAR.</summary>
    ProductRecipe = 0,

    /// <summary>Kanal reçetesi (N11/Trendyol/Etsy) — CANLI kullanım; silmeyi BLOKLAR.</summary>
    ChannelRecipe = 1,

    /// <summary>Reçete şablonu — TASLAKtır, canlı satış değildir; yalnız UYARI, silmeyi BLOKLAMAZ.
    /// <i>Kullanılmayan bir şablon yüzünden emtia silinememesi orantısız olurdu; şablon zaten uygulanırken
    /// hata verir.</i></summary>
    RecipeTemplate = 2,
}

/// <summary>Bir emtiayı reçetesinde taşıyan kayıt — silme/pasifleştirme uyarısında kullanıcıya gösterilir.
/// Kod/ad kullanıcıya anlamlı mesaj kurmak için taşınır (id listesi uyarı metni olamaz).
/// <para><see cref="VariantIds"/> yalnız ürün reçetesinde doludur; şablon/kanal kullanımında boştur.</para></summary>
public sealed record CommodityUsage(
    CommodityUsageKind Kind,
    Guid OwnerId,
    string OwnerCode,
    string OwnerName,
    IReadOnlyList<Guid> VariantIds)
{
    /// <summary>Bu kullanım silmeyi engeller mi. Şablon TASLAK olduğu için engellemez — kullanıcı uyarılır,
    /// karar onundur.</summary>
    public bool BlocksDeletion
    {
        get { return Kind != CommodityUsageKind.RecipeTemplate; }
    }
}

/// <summary>Etkilenen ürün: muadil mi (yeniden üretim) değil mi (satılabilir adet), hangi varyantları değişti.</summary>
public sealed record AffectedProduct(
    Guid ProductId,
    bool IsSubstitution,
    Guid? SubstitutionGroupId,
    List<Guid> AffectedVariantIds);

file readonly record struct VariantOwner(Guid ProductId, Guid VariantId);
