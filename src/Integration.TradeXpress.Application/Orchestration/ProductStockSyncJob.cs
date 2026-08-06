using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Orchestration;

/// <summary>Job'ın tetiklenme nedeni — hangi adımların koşacağını belirler (2026-07-25 inceleme bulgusu #11:
/// 15-dk repricing turu stok hesabını da koşturuyordu — fiyat tazelemesi için gereksiz DB yükü + audit gürültüsü).</summary>
public enum ProductSyncReason
{
    /// <summary>Maden stoğu değişti (VoucherLine tetiği) → stok yeniden-hesap + push.</summary>
    StockChanged = 0,

    /// <summary>15-dk fiyat döngüsü → YALNIZ push (N11 senkronu güncel kurdan fiyatı zaten türetir);
    /// stok hesabı atlanır — stok değişmediyse sonuç aynıydı, değiştiyse StockChanged tetiği zaten koşmuştur.</summary>
    Repricing = 1,
}

/// <summary>Ürün-başına stok senkron job argümanı — dar kapsam (ADR: kilitlenmezlik iş tasarımından).</summary>
public class ProductStockSyncJobArgs
{
    public Guid? TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProductId { get; set; }
    public ProductSyncReason Reason { get; set; }
}

/// <summary>
/// Müdürün YARDIMCISI: tek ürünün stok gerçeğini kanala taşır (ADR-PRODUCT-ORCHESTRATION Dilim 1).
/// <list type="number">
///   <item><b>Muadil</b> → <see cref="SubstitutionVariantMaterializer"/>: varyantlar o anki stoğa göre
///   yeniden üretilir (Tek: Rank1→ana reçete; Çoklu: adaylar ayrı varyant).</item>
///   <item><b>Calculated (muadil değil)</b> → <see cref="SellableStockCalculator"/>: her varyantın
///   satılabilir adedi reçete darboğazından hesaplanır, <c>EntityVariant.StockQuantity</c>'ye yazılır
///   (push zinciri <c>OverrideStock ?? StockQuantity</c> okur — kanal-özel elle override'a dokunulmaz).</item>
///   <item>Kanal push — <see cref="IChannelStockPusher"/> (N11 dirty-check'li; hata job'ı düşürmez).</item>
/// </list>
/// İDEMPOTENT: aynı ürün için art arda koşmak aynı sonuca varır — fazla push'u N11 dirty-check eler.
/// STOK tetiğinden Fixed/Unlimited GELMEZ (ters-endeks eledi); REPRICING tetiğinden HERKES gelir —
/// o yolda stok hesabı atlanır, yalnız push (fiyat tazeleme) yapılır.
/// </summary>
public class ProductStockSyncJob : AsyncBackgroundJob<ProductStockSyncJobArgs>, ITransientDependency
{
    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly SubstitutionVariantMaterializer _materializer;
    private readonly ICommodityStockReader _stockReader;
    private readonly IChannelStockPusher _channelPusher;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentCompany _currentCompany;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly OrchestrationIdentityScope _identityScope;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly ILogger<ProductStockSyncJob> _logger;

    private const string ProductEntityName = "Product";

    public ProductStockSyncJob(
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        SubstitutionVariantMaterializer materializer,
        ICommodityStockReader stockReader,
        IChannelStockPusher channelPusher,
        ICurrentTenant currentTenant,
        ICurrentCompany currentCompany,
        IUnitOfWorkManager unitOfWorkManager,
        IAsyncQueryableExecuter asyncExecuter,
        OrchestrationIdentityScope identityScope,
        ICurrentPrincipalAccessor currentPrincipalAccessor,
        ILogger<ProductStockSyncJob> logger)
    {
        _productRepository = productRepository;
        _variantRepository = variantRepository;
        _recipeLineRepository = recipeLineRepository;
        _materializer = materializer;
        _stockReader = stockReader;
        _channelPusher = channelPusher;
        _currentTenant = currentTenant;
        _currentCompany = currentCompany;
        _unitOfWorkManager = unitOfWorkManager;
        _asyncExecuter = asyncExecuter;
        _identityScope = identityScope;
        _currentPrincipalAccessor = currentPrincipalAccessor;
        _logger = logger;
    }

    public override async Task ExecuteAsync(ProductStockSyncJobArgs args)
    {
        using (_currentTenant.Change(args.TenantId))
        using (_currentCompany.Change(args.CompanyId))
        {
            // KİMLİK (2026-07-25 inceleme bulgusu #1 — zinciri kökten kıran hata): job kimliksiz koşar ama
            // zincirin app-service'leri ([Authorize]'lı muadil hesabı + N11 senkronu) yetki ister — kimliksiz
            // her tetik AbpAuthorizationException'dı (push tarafında SESSİZCE yutuluyordu). Tenant admin'i
            // impersonate edilir; [Authorize] gevşetilmez (§2). Change BU frame'de kurulur — AsyncLocal
            // kuralı (OrchestrationIdentityScope sınıf yorumu): await edilen metodun içinden geri akmaz.
            var adminPrincipal = await _identityScope.BuildTenantAdminPrincipalAsync();
            if (adminPrincipal is null)
            {
                _logger.LogWarning(
                    "Orkestrasyon job'ı atlandı: tenant admin bulunamadı (Tenant={TenantId}, Product={ProductId}).",
                    args.TenantId, args.ProductId);
                return;
            }

            using (_currentPrincipalAccessor.Change(adminPrincipal))
            {
                // 1) STOK YENİDEN-HESAP kendi UoW'unda commit edilir — push (dış HTTP) bu transaction'ın DIŞINDA
                //    kalır (ADR senkronluk sözleşmesi: push hatası hesap sonucunu geri almasın).
                //    Repricing tetiğinde bu adım ATLANIR (bulgu #11): stok değişmedi, yalnız fiyat tazelenecek.
                if (args.Reason == ProductSyncReason.StockChanged)
                {
                    using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                    {
                        var product = await _productRepository.FindAsync(args.ProductId);
                        if (product is null)
                        {
                            return;   // silinmiş — iş yok (idempotent çıkış)
                        }

                        // STOK hesabı yalnız Calculated'da (Fixed/Unlimited'ın stoğuna DOKUNULMAZ — Hakan kararı);
                        // PUSH ise politikadan bağımsız (repricing döngüsü Fixed ürünün FİYATINI da tazeler).
                        if (product.StockPolicy == ProductStockPolicy.Calculated)
                        {
                            if (product.VariantMode == ProductVariantMode.Substitution)
                            {
                                await _materializer.MaterializeAsync(product);
                            }
                            else
                            {
                                await RecalculateSellableStockAsync(product);
                            }
                        }

                        await uow.CompleteAsync();
                    }
                }

                // 2) Kanal push — commit SONRASI; hata fırlatmaz (pusher loglar, sonraki tetik telafi eder).
                await _channelPusher.PushProductAsync(args.ProductId);
            }
        }
    }

    /// <summary>Muadil olmayan Calculated ürün: her varyantın satılabilir adedi = reçete darboğazı
    /// (<see cref="SellableStockCalculator"/>). Stok-taşıyan satırı olmayan varyant (null) DOKUNULMAZ.</summary>
    private async Task RecalculateSellableStockAsync(Product product)
    {
        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id));
        if (variants.Count == 0)
        {
            return;
        }

        var variantIds = variants.Select(v => v.Id).ToList();
        var lines = await _asyncExecuter.ToListAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => variantIds.Contains(l.ProductVariantId)
                            && l.ComponentType == RecipeComponentType.CatalogCommodity
                            && l.CommodityProcessType != null
                            && CommodityStockFamilies.Tracked.Contains(l.CommodityProcessType!.Value)
                            && l.CommodityId != null));

        if (lines.Count == 0)
        {
            return;   // reçete stoğa bağlı değil — kanal stoğuna dokunma
        }

        // Aile BAŞINA okuma: her ailenin kendi stok raporu var. Anahtar aileyi taşıdığından sonuçlar tek
        // sözlükte güvenle birleşir (aynı Guid iki ailede çakışsa bile ayrı havuz kalır).
        var available = new Dictionary<CommodityStockKey, CommodityAvailability>();
        foreach (var familyGroup in lines.GroupBy(l => l.CommodityProcessType!.Value))
        {
            var ids = familyGroup.Select(l => l.CommodityId!.Value).Distinct().ToList();
            foreach (var (key, value) in await _stockReader.GetAvailableAsync(familyGroup.Key, ids))
            {
                available[key] = value;
            }
        }

        var linesByVariant = lines.GroupBy(l => l.ProductVariantId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var variant in variants)
        {
            if (!linesByVariant.TryGetValue(variant.Id, out var variantLines))
            {
                continue;
            }

            var requirements = variantLines.ConvertAll(ToRequirement);

            var sellable = SellableStockCalculator.Calculate(requirements, available);
            if (sellable is { } count && count != variant.StockQuantity)
            {
                variant.SetStock(count);
                await _variantRepository.UpdateAsync(variant, autoSave: true);
            }
        }
    }

    /// <summary>Reçete satırı → stok ihtiyacı. <b>Aile başına boyut seçimi burada yapılır</b> (birim tuzağı):
    /// <list type="bullet">
    ///   <item><b>Metal</b> — YALNIZ gram (<c>Amount</c>). Adetli madende <c>Amount = Quantity × StableQuantity</c>
    ///   olduğundan gram zaten tam kısıttır; adedi de kısıt saymak aynı stoğu iki kez daraltırdı. (Mevcut
    ///   davranış — 2026-08-06 rename'inde bilinçli olarak DEĞİŞTİRİLMEDİ.)</item>
    ///   <item><b>Good</b> — satırın BEYAN ETTİĞİ her boyut. Mamül hem adetle hem stok-birimi miktarıyla
    ///   izlenebilir; hangisinin "asıl" olduğunu varsaymak yerine dolu olan her boyut kısıt sayılır (0 olan
    ///   zaten kısıt getirmez). Varsayım yapıp yanlış boyutu seçmek, bu oturumun avladığı sessiz-yanlış-rakam
    ///   deseninin ta kendisidir.</item>
    /// </list></summary>
    private static RecipeCommodityRequirement ToRequirement(ProductVariantRecipeLine line)
    {
        var family = line.CommodityProcessType!.Value;
        var requiredQuantity = family == ProcessType.Metal ? 0m : line.Quantity;

        return new RecipeCommodityRequirement(
            family, line.CommodityId!.Value, line.CommodityVariantId, line.Amount, requiredQuantity);
    }
}
