using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Goods;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Orchestration;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Products;

/// <summary>
/// SİHİRBAZ SINIFLANDIRMASININ SUNUCU TARAFI — içe aktarılmış, reçetesiz bir ürünü emtiaya bağlar
/// (2026-08-05 Hakan kararları).
///
/// <para><b>Neden gerekli:</b> mağaza içe aktarımı ürünü <c>StockPolicy=Fixed</c> + reçetesiz getiriyor.
/// Orkestrasyonun hesaplayacağı bir şey olmuyor, pazaryerinin eski adedi geçerli olmayı sürdürüyor ve
/// aşırı satış savunmalarının hiçbiri o üründe ateşlemiyor.</para>
///
/// <para><b>Sınıflandırma MANUELDİR, yazılım TAHMİN ETMEZ:</b> aileyi kullanıcı seçer. Ön-doldurma yalnız
/// kod/ad düzeyindedir — bir ürünün "hangi emtiadan yapıldığı" veriden çıkarılamaz.</para>
///
/// <para><b>Ürün <c>Draft</c> KALIR:</b> sınıflandırma satışa açmaz. Güvenlik zorunluluktan değil
/// STATÜDEN gelir — reçete kurulduktan sonra bir insan doğrular (<c>ProductVariantDetail.Verify</c>).</para>
///
/// <para><b>Emtia kaydı ailenin KENDİ app service'iyle açılır</b> (repository ile DEĞİL): şirket sahipliği
/// damgası (<c>CompanyOwnershipGuard</c>), kod normalizasyonu ve benzersizlik kontrolü orada yaşıyor;
/// repository'ye inmek üçünü birden atlardı.</para>
/// </summary>
public class ProductCommodityProvisioner : ITransientDependency
{
    private const string ProductEntityName = "Product";
    private const int MaxCodeAttempts = 20;

    private readonly IRepository<Product, Guid> _productRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly ProductRecipeLineWriter _recipeLineWriter;
    private readonly ChannelOverrideAuthority _overrideAuthority;
    private readonly IMetalAppService _metals;
    private readonly IScrapAppService _scraps;
    private readonly IFutureAppService _futures;
    private readonly IJewelryAppService _jewelries;
    private readonly IStoneAppService _stones;
    private readonly IGoodAppService _goods;
    private readonly IServiceAppService _services;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly ICurrentCompany _currentCompany;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ILogger<ProductCommodityProvisioner> _logger;

    public ProductCommodityProvisioner(
        IRepository<Product, Guid> productRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        ProductRecipeLineWriter recipeLineWriter,
        ChannelOverrideAuthority overrideAuthority,
        IMetalAppService metals,
        IScrapAppService scraps,
        IFutureAppService futures,
        IJewelryAppService jewelries,
        IStoneAppService stones,
        IGoodAppService goods,
        IServiceAppService services,
        IBackgroundJobManager backgroundJobManager,
        ICurrentCompany currentCompany,
        ICurrentTenant currentTenant,
        IAsyncQueryableExecuter asyncExecuter,
        ILogger<ProductCommodityProvisioner> logger)
    {
        _productRepository    = productRepository;
        _variantRepository    = variantRepository;
        _recipeLineRepository = recipeLineRepository;
        _recipeLineWriter     = recipeLineWriter;
        _overrideAuthority    = overrideAuthority;
        _metals               = metals;
        _scraps               = scraps;
        _futures              = futures;
        _jewelries            = jewelries;
        _stones               = stones;
        _goods                = goods;
        _services             = services;
        _backgroundJobManager = backgroundJobManager;
        _currentCompany       = currentCompany;
        _currentTenant        = currentTenant;
        _asyncExecuter        = asyncExecuter;
        _logger               = logger;
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Aday listesi
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Çalışılan şirketteki REÇETESİZ ürünler. Kanal filtresi YOK (bilinçli): eski içe
    /// aktarımların bıraktığı ürünler de bu listede görünmeli, yoksa sonsuza dek kayıp kalırlar.</summary>
    public virtual async Task<List<ProductCommodityCandidateDto>> GetCandidatesAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<ProductCommodityCandidateDto>();
        }

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName)
                .Select(v => new { v.Id, v.EntityId }));
        if (variants.Count == 0)
        {
            return new List<ProductCommodityCandidateDto>();
        }

        var variantIds = variants.ConvertAll(v => v.Id);
        var withRecipe = (await _asyncExecuter.ToListAsync(
                (await _recipeLineRepository.GetQueryableAsync())
                    .Where(l => variantIds.Contains(l.ProductVariantId))
                    .Select(l => l.ProductVariantId)
                    .Distinct()))
            .ToHashSet();

        // Reçetesi TAMAMEN olmayan ürün: hiçbir varyantında satır yok. Bir varyantı kurulmuş ürüne
        // dokunmayız — kullanıcının yarım bıraktığı emek burada toplu işleme girmemeli.
        var classifiedProductIds = variants
            .Where(v => withRecipe.Contains(v.Id))
            .Select(v => v.EntityId)
            .ToHashSet();

        var variantCountByProduct = variants
            .GroupBy(v => v.EntityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var products = await _asyncExecuter.ToListAsync(
            (await _productRepository.GetQueryableAsync())
                .Where(p => p.CompanyId == companyId)
                .Select(p => new { p.Id, p.Code, p.Name }));

        return products
            .Where(p => !classifiedProductIds.Contains(p.Id))
            .Select(p => new ProductCommodityCandidateDto
            {
                ProductId    = p.Id,
                Code         = p.Code,
                Name         = p.Name,
                VariantCount = variantCountByProduct.GetValueOrDefault(p.Id),
            })
            .OrderBy(p => p.Code)
            .ToList();
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Uygulama
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Sınıflandırmayı uygular. Bir satırın hatası DİĞERLERİNİ DÜŞÜRMEZ — gerekçe
    /// <see cref="ProductCommodityProvisionResultDto.Issues"/>'a yazılır; toplu ekranda tek bozuk satırın
    /// 100 ürünlük işi çöpe atması kabul edilemez.</summary>
    public virtual async Task<ProductCommodityProvisionResultDto> ProvisionAsync(
        ProductCommodityProvisionInputDto input)
    {
        var result = new ProductCommodityProvisionResultDto();
        if (input.Items.Count == 0)
        {
            return result;
        }

        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Product:CompanyContextRequired");
        }

        foreach (var item in input.Items)
        {
            try
            {
                await ProvisionOneAsync(companyId, item, result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Ürün sınıflandırması başarısız: Product={ProductId}, Family={Family}.",
                    item.ProductId, item.Family);
                result.Issues.Add($"{item.ProductId}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task ProvisionOneAsync(
        Guid companyId, ProductCommodityProvisionItemDto item, ProductCommodityProvisionResultDto result)
    {
        var product = await _productRepository.FindAsync(item.ProductId);
        if (product is null || product.CompanyId != companyId)
        {
            result.Issues.Add($"{item.ProductId}: ürün bulunamadı.");
            return;
        }

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductEntityName && v.EntityId == product.Id)
                .Select(v => v.Id));
        if (variants.Count == 0)
        {
            result.Issues.Add($"{product.Code}: varyantı yok, reçete bağlanamaz.");
            return;
        }

        // Kullanıcı emeğini EZME: aralarında reçetesi olan varyant varsa bu ürüne dokunma.
        var alreadyHasRecipe = await _asyncExecuter.AnyAsync(
            (await _recipeLineRepository.GetQueryableAsync())
                .Where(l => variants.Contains(l.ProductVariantId)));
        if (alreadyHasRecipe)
        {
            result.Issues.Add($"{product.Code}: zaten reçetesi var, atlandı.");
            return;
        }

        var isService = item.Family == ProcessType.Service;

        var commodityId = item.Mode == ProductCommodityProvisionMode.UseExisting
            ? item.ExistingCommodityId
            : await CreateCommodityAsync(item, product, result);

        if (commodityId is null || commodityId == Guid.Empty)
        {
            result.Issues.Add($"{product.Code}: emtia çözülemedi ({item.Family}).");
            return;
        }

        // Reçete satırı ürünün TÜM varyantlarına yazılır: sınıflandırma ürün seviyesinde verilir ve
        // varyantların hepsi aynı emtiadan yapılır (14/18/22 ayar aynı madeni tüketir, miktarı farklıdır —
        // miktar ayarı varyant ekranında incelenir).
        foreach (var variantId in variants)
        {
            var line = BuildLine(item, commodityId.Value, isService);
            await _recipeLineWriter.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto> { line });
            result.CreatedRecipeLines++;
        }

        // HİZMET tek başınaysa stok kavramı YOKTUR (2026-08-05 karar #7): Calculated yapmak, stok zincirinin
        // hiç veri bulamayacağı bir hesap açardı — sonuç sessizce 0 olurdu. Unlimited dürüst olandır.
        product.SetStockPolicy(isService ? ProductStockPolicy.Unlimited : ProductStockPolicy.Calculated);
        await _productRepository.UpdateAsync(product, autoSave: true);

        // OTORİTE DEVRİ: pazaryerinin içe aktarımda yazdığı stok/fiyat aynası artık geçersiz.
        result.ClearedChannelOverrides += await _overrideAuthority.TransferAuthorityAsync(product.Id);

        // Stok yeniden-hesabı: Unlimited üründe job stok adımını zaten atlar, push'u yapar (fiyat tazeleme).
        await _backgroundJobManager.EnqueueAsync(new ProductStockSyncJobArgs
        {
            TenantId  = _currentTenant.Id,
            CompanyId = companyId,
            ProductId = product.Id,
            Reason    = ProductSyncReason.StockChanged,
        });

        result.ProvisionedProducts++;
    }

    /// <summary>Reçete satırı grafı — katalog emtiası ya da hizmet.
    /// <para><b>Hizmet bedeli 0 bırakılır ve UYDURULMAZ:</b> sihirbaz ücreti bilemez. Ürün <c>Draft</c>
    /// kaldığı için satışa çıkmaz; bedel varyant ekranında girilir.</para></summary>
    private static ProductRecipeLineGraphDto BuildLine(
        ProductCommodityProvisionItemDto item, Guid commodityId, bool isService)
    {
        if (isService)
        {
            return new ProductRecipeLineGraphDto
            {
                ComponentType    = RecipeComponentType.Service,
                CommodityId      = commodityId,
                DerivedBaseMode  = RecipeDerivedBaseMode.AllAbove,
                DerivedOperation = RecipeDerivedOperation.Percent,
                DerivedOperand   = 0m,
                LineOrder        = 0,
            };
        }

        return new ProductRecipeLineGraphDto
        {
            ComponentType        = RecipeComponentType.CatalogCommodity,
            CommodityProcessType = item.Family,
            CommodityId          = commodityId,
            Quantity             = item.Quantity,
            Amount               = item.Amount,
            Factor               = 1m,
            PaymentType          = ProcessPaymentType.Normal,
            LineOrder            = 0,
        };
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  Katalog kaydı oluşturma — aile başına KENDİ app service'i
    // ────────────────────────────────────────────────────────────────────────────────

    private async Task<Guid?> CreateCommodityAsync(
        ProductCommodityProvisionItemDto item, Product product, ProductCommodityProvisionResultDto result)
    {
        var name = string.IsNullOrWhiteSpace(item.Name) ? product.Name : item.Name!;
        var baseCode = string.IsNullOrWhiteSpace(item.Code) ? product.Code : item.Code!;

        // Doğal birim Metal/Scrap/Future'da ZORUNLU ve ön-doldurulamaz: hangi birimin takip edildiği bir iş
        // kararıdır, üründen türetilemez. Eksikse SESSİZ varsayılan konmaz — satır atlanır.
        var needsFollowingUnit = item.Family is ProcessType.Metal or ProcessType.Scrap or ProcessType.Future;
        if (needsFollowingUnit && item.FollowingUnitId is null)
        {
            result.Issues.Add($"{product.Code}: {item.Family} için doğal birim (takip birimi) seçilmedi.");
            return null;
        }

        var created = await CreateWithUniqueCodeAsync(item.Family, baseCode, name, item.FollowingUnitId);
        if (created is not null)
        {
            result.CreatedCommodities++;
        }

        return created;
    }

    /// <summary>Kod çakışmasını benzersizleştirme son-ekiyle çözer. Kodun benzersizliği ailenin app
    /// service'inde zorlanır (şirket-scope) — burada tekrar SORGULANMAZ, denenip sonucuna bakılır:
    /// paralel bir kayıt araya girse bile doğru davranış aynı kalır.</summary>
    private async Task<Guid?> CreateWithUniqueCodeAsync(
        ProcessType family, string baseCode, string name, Guid? followingUnitId)
    {
        for (var attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            var code = attempt == 1 ? baseCode : $"{baseCode}-{attempt}";
            try
            {
                return await CreateOfFamilyAsync(family, code, name, followingUnitId);
            }
            catch (BusinessException ex) when (IsCodeConflict(ex))
            {
                // Aynı kod başka bir emtiada kullanılıyor — son-ek artırılıp yeniden denenir.
            }
        }

        throw new BusinessException("TradeXpress:Product:CommodityCodeExhausted")
            .WithData("Code", baseCode);
    }

    private static bool IsCodeConflict(BusinessException ex)
    {
        return ex.Code is { Length: > 0 } code && code.EndsWith("CodeAlreadyExists", StringComparison.Ordinal);
    }

    private async Task<Guid?> CreateOfFamilyAsync(
        ProcessType family, string code, string name, Guid? followingUnitId)
    {
        switch (family)
        {
            case ProcessType.Metal:
                return (await _metals.CreateAsync(new MetalCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = followingUnitId,
                })).Id;

            case ProcessType.Scrap:
                return (await _scraps.CreateAsync(new ScrapCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = followingUnitId,
                })).Id;

            case ProcessType.Future:
                return (await _futures.CreateAsync(new FutureCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = followingUnitId,
                })).Id;

            case ProcessType.Jewelry:
                return (await _jewelries.CreateAsync(new JewelryCreateDto { Code = code, Name = name })).Id;

            case ProcessType.Stone:
                return (await _stones.CreateAsync(new StoneCreateDto { Code = code, Name = name })).Id;

            case ProcessType.Good:
                return (await _goods.CreateAsync(new GoodCreateDto { Code = code, Name = name })).Id;

            case ProcessType.Service:
                return (await _services.CreateAsync(new ServiceCreateDto { Code = code, Name = name })).Id;

            default:
                throw new BusinessException("TradeXpress:Product:CommodityFamilyNotProvisionable")
                    .WithData("Family", family.ToString());
        }
    }
}
