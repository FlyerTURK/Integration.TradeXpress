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
/// STATÜDEN gelir — reçete kurulduktan sonra bir insan doğrular
/// (<c>IProductAppService.VerifySaleReadinessAsync</c> → <c>ProductVariantDetail.MarkVerified</c>).</para>
///
/// <para><b>Emtia kaydı ailenin KENDİ app service'iyle açılır</b> (repository ile DEĞİL): şirket sahipliğinin
/// çözümü (<c>CompanyOwnershipGuard.ResolveOwnerCompanyId</c>), kod normalizasyonu ve benzersizlik kontrolü orada yaşıyor;
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
    private readonly ProductToGoodProjector _goodProjector;
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
        ProductToGoodProjector goodProjector,
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
        _goodProjector        = goodProjector;
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

        // ── SIFIR ADET + SIFIR MİKTAR GUARD'I, KATALOG KAYDI AÇILMADAN ÖNCE (2026-08-19) ────────────────
        //
        // Kural ProductRecipeLineWriter'da zaten fırlatılıyor; ama bu yol yazıcıya ulaşmadan ÖNCE
        // CreateCommodityAsync ile yeni emtia kaydını autoSave'le yazıyor. Guard yalnız yazıcıda kalsaydı
        // 0/0'lık karar önce katalog kaydını açar, sonra yazıcı reddeder; ProvisionAsync'in satır-başı
        // catch'i hatayı Issues'a yazıp yoluna devam ettiği için UoW geri almaz → YETİM EMTİA kalır, ürün
        // reçetesiz/Draft kalır ve rapora kodlu BusinessException'ın anlamsız .Message'ı düşerdi.
        // Başlık formu 0/0'ı engelliyor ama satır-başı düzenleme (SetRowAmount/SetRowQuantity) kararı sonradan
        // 0/0'a çekebiliyor; sunucu tarafı UI'a güvenmez. Kuralın kendisi Domain'de TEK yerde
        // (RecipeLineQuantityRule), burada yalnız sorulur. Diğer ön-kontrollerle aynı dil: Issues + return.
        var componentType = isService ? RecipeComponentType.Service : RecipeComponentType.CatalogCommodity;
        if (!RecipeLineQuantityRule.IsSatisfied(componentType, item.Quantity, item.Amount))
        {
            result.Issues.Add(
                $"{product.Code}: {item.Family} ailesinde adet ya da miktardan en az biri sıfırdan büyük olmalıdır "
                + "(0 adet + 0 miktar hiçbir şey temsil etmez; emtia kaydı açılmadı).");
            return;
        }

        var commodityId = item.Mode switch
        {
            ProductCommodityProvisionMode.UseExisting => item.ExistingCommodityId,
            _ => await CreateCommodityAsync(item, product, result),
        };

        if (commodityId is null || commodityId == Guid.Empty)
        {
            result.Issues.Add($"{product.Code}: emtia çözülemedi ({item.Family}).");
            return;
        }

        // Reçete satırı ürünün TÜM varyantlarına yazılır: sınıflandırma ürün seviyesinde verilir ve
        // varyantların hepsi aynı emtiadan yapılır (14/18/22 ayar aynı madeni tüketir, miktarı farklıdır —
        // miktar ayarı varyant ekranında incelenir).
        // DEĞERLEME BİRİMİ (2026-08-06 Hakan tespiti): satır bu alan olmadan maliyet üretemez —
        // ProductRecipeCostCalculator birimi çözemeyince satırı MissingRate işaretler ("Kur yok") ve
        // maliyeti NULL döndürür; ürün 1300 TL'lik mamülle 0 TRY görünür. Fiyat biliniyordu, birim yoktu.
        var snapshot = await ResolveCatalogSnapshotAsync(item, commodityId.Value, isService);

        foreach (var variantId in variants)
        {
            var line = BuildLine(item, commodityId.Value, isService, snapshot.UnitId, snapshot.Factor);
            await _recipeLineWriter.SaveAsync(companyId, variantId, new List<ProductRecipeLineGraphDto> { line });
            result.CreatedRecipeLines++;
        }

        // HİZMET tek başınaysa stok kavramı YOKTUR (2026-08-05 karar #7): Calculated yapmak, stok zincirinin
        // hiç veri bulamayacağı bir hesap açardı — sonuç sessizce 0 olurdu. Unlimited dürüst olandır.
        product.SetStockPolicy(isService ? ProductStockPolicy.Unlimited : ProductStockPolicy.Calculated);
        await _productRepository.UpdateAsync(product, autoSave: true);

        // OTORİTE DEVRİ: pazaryerinin içe aktarımda yazdığı stok/fiyat yansıması artık geçersiz.
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
    /// <summary>Satırın DEĞERLEME BİRİMİ — maliyet motorunun fiyatı hangi birimden rebase edeceği.
    /// <list type="bullet">
    ///   <item><b>Metal-legged</b> (<c>ProductRecipeCostCalculator.IsMetalLegged</c>; Metal/Scrap/Future): doğal birim = <c>FollowingUnitId</c>.</item>
    ///   <item><b>Parasal</b> (Good/Jewelry/Stone): giriş fiyatının birimi. Mamülde fiyat VARYANTTA
    ///   yaşadığı için ana varyanttan okunur (yoksa ilk varyant).</item>
    ///   <item><b>Hizmet</b>: null — bedel <c>ManualUnitId</c> yolundan gelir.</item>
    /// </list>
    /// <para>Çözülemezse null döner ve satır "Kur yok" olarak İŞARETLENİR — sessiz sıfır DEĞİL.</para></summary>
    /// <summary>Satırın KATALOG SNAPSHOT'I: değerleme birimi + milyem/çarpan.
    ///
    /// <para><b>İkisi de KATALOG KAYDINDAN okunur, uydurulmaz</b> (2026-08-06). Milyem sabit <c>1</c>
    /// yazılıyordu ve maliyet motoru metal-legged satırda <c>gram × Factor</c> hesapladığı için 22 ayar
    /// (0.916) yerine 1 kullanmak maden bacağını ~%9 ŞİŞİRİYORDU — hatasız, uyarısız. Kullanıcının beyan
    /// ettiği değer varsa o esastır; yoksa kaydın kendi değeri alınır.</para>
    ///
    /// <para>Parasal ailelerde (Good/Jewelry/Stone) <c>Factor</c> maliyet yolunda KULLANILMAZ (fiyat
    /// EntryPrice'tan gelir) — orada 1 nötr bir değerdir, tahmin değil.</para></summary>
    private async Task<(Guid? UnitId, decimal Factor)> ResolveCatalogSnapshotAsync(
        ProductCommodityProvisionItemDto item, Guid commodityId, bool isService)
    {
        if (isService)
        {
            return (null, 1m);
        }

        switch (item.Family)
        {
            case ProcessType.Metal:
                var metal = await _metals.GetAsync(commodityId);
                return (metal.FollowingUnitId, item.Factor ?? metal.Factor);

            case ProcessType.Scrap:
                var scrap = await _scraps.GetAsync(commodityId);
                return (scrap.FollowingUnitId, item.Factor ?? scrap.Factor);

            case ProcessType.Future:
                var future = await _futures.GetAsync(commodityId);
                return (future.FollowingUnitId, item.Factor ?? future.FollowingFactor);

            case ProcessType.Good:
                var good = await _goods.GetAsync(commodityId);
                var goodVariant = good.Variants.FirstOrDefault(v => v.IsMain) ?? good.Variants.FirstOrDefault();
                return (goodVariant?.EntryPriceUnitId, 1m);

            case ProcessType.Jewelry:
                return ((await _jewelries.GetAsync(commodityId)).EntryPriceUnitId, 1m);

            case ProcessType.Stone:
                return ((await _stones.GetAsync(commodityId)).EntryPriceUnitId, 1m);

            default:
                return (null, 1m);
        }
    }

    private static ProductRecipeLineGraphDto BuildLine(
        ProductCommodityProvisionItemDto item, Guid commodityId, bool isService, Guid? valuationUnitId,
        decimal factor)
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
            Factor               = factor,
            ValuationUnitId      = valuationUnitId,
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

        // ── MİLYEM SORULUR, UYDURULMAZ (2026-08-06) ──────────────────────────────────────────────────
        //
        // Panel eskiden milyemi hiç sormuyor, entity varsayılanına düşüyordu: Maden 0.995, Hurda 0.570.
        // O sayı MAKUL GÖRÜNDÜĞÜ için kimse fark etmez — oysa 22 ayar bilezik 0.916'dır ve o andan sonra
        // her değerleme, her reçete maliyeti yanlış milyemle hesaplanır. Bu, oturum boyunca avlanan
        // "eksik veriye makul bir sayı koy" hatasının ta kendisiydi.
        //
        // Önce metal-legged ailede hızlı-açmayı YASAKLAMAYI önerdim; kullanıcı kısıtı kaldırdı
        // ("createnew de serbest olmalı metalde"). Delik yasakla değil BEYANLA kapandı: metal-legged
        // ailede yeni kayıt açmak için Factor ZORUNLU. Sistem tahmin etmez, kullanıcı söyler.
        //
        // KLON bu şartın dışındadır — kopya değeri GERÇEK bir kayıttan devralır, uydurma yoktur.
        if (IsMetalLegged(item.Family)
            && item.Mode == ProductCommodityProvisionMode.CreateNew
            && item.Factor is null)
        {
            result.Issues.Add(
                $"{product.Code}: {item.Family} ailesinde yeni emtia için MİLYEM zorunludur "
                + "(varsayılana düşerse değerleme sessizce yanlış olur).");
            return null;
        }

        var created = item.Mode == ProductCommodityProvisionMode.CloneExisting
            ? await CloneWithUniqueCodeAsync(item, baseCode, name, result, product)
            : await CreateWithUniqueCodeAsync(item, baseCode, name, product.Id);
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
        ProductCommodityProvisionItemDto item, string baseCode, string name, Guid productId)
    {
        return await WithUniqueCodeAsync(baseCode, code => CreateOfFamilyAsync(item, code, name, productId));
    }

    /// <summary>KLON: mevcut kaydı ŞABLON alıp yeni kod/adla kopyalar (2026-08-06 Hakan isteği).
    /// <para>Değerler GERÇEK bir kayıttan gelir — milyem, adet-gram katsayısı, işçilik/fiyat ayarları
    /// kullanıcının daha önce doğruladığı hâliyle taşınır. Bu yüzden klon, metal-legged ailelerde de
    /// güvenlidir: ortada uydurulmuş sayı yoktur.</para></summary>
    private async Task<Guid?> CloneWithUniqueCodeAsync(
        ProductCommodityProvisionItemDto item, string baseCode, string name,
        ProductCommodityProvisionResultDto result, Product product)
    {
        if (item.ExistingCommodityId is not { } sourceId || sourceId == Guid.Empty)
        {
            result.Issues.Add($"{product.Code}: klonlanacak kaynak emtia seçilmedi.");
            return null;
        }

        return await WithUniqueCodeAsync(baseCode, code => CloneOfFamilyAsync(item.Family, sourceId, code, name));
    }

    /// <summary>Kod çakışmasını benzersizleştirme son-ekiyle çözer. Kodun benzersizliği ailenin app
    /// service'inde zorlanır (şirket-scope) — burada tekrar SORGULANMAZ, denenip sonucuna bakılır:
    /// paralel bir kayıt araya girse bile doğru davranış aynı kalır.</summary>
    private static async Task<Guid?> WithUniqueCodeAsync(string baseCode, Func<string, Task<Guid?>> factory)
    {
        for (var attempt = 1; attempt <= MaxCodeAttempts; attempt++)
        {
            var code = attempt == 1 ? baseCode : $"{baseCode}-{attempt}";
            try
            {
                return await factory(code);
            }
            catch (BusinessException ex) when (IsCodeConflict(ex))
            {
                // Aynı kod başka bir emtiada kullanılıyor — son-ek artırılıp yeniden denenir.
            }
        }

        throw new BusinessException("TradeXpress:Product:CommodityCodeExhausted")
            .WithData("Code", baseCode);
    }

    /// <summary>Milyem/katsayı taşıyan aileler — <c>ProductRecipeCostCalculator.IsMetalLegged</c> ile AYNI küme.
    /// Bu ailelerde eksik katsayı SESSİZ yanlış değerleme demektir (bkz. yeni kayıt guard'ı).</summary>
    private static bool IsMetalLegged(ProcessType family)
    {
        return family is ProcessType.Metal or ProcessType.Scrap or ProcessType.Future;
    }

    private static bool IsCodeConflict(BusinessException ex)
    {
        return ex.Code is { Length: > 0 } code && code.EndsWith("CodeAlreadyExists", StringComparison.Ordinal);
    }

    /// <summary>Yeni katalog kaydı — ailenin KENDİ app service'iyle (<c>CompanyOwnershipGuard</c> + kod benzersizliği orada).
    /// <para>Metal-legged ailelerde <c>Factor</c> çağıran tarafından ZORUNLU kılınmıştır; buraya null
    /// geldiğinde entity varsayılanı devreye girerdi, o yüzden guard yukarıda.</para></summary>
    private async Task<Guid?> CreateOfFamilyAsync(ProductCommodityProvisionItemDto item, string code, string name, Guid productId)
    {
        var unit = item.FollowingUnitId;
        var stable = item.StableQuantity ?? 0m;

        switch (item.Family)
        {
            case ProcessType.Metal:
                return (await _metals.CreateAsync(new MetalCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = unit,
                    Factor = item.Factor ?? MetalConsts.DefaultFactor,
                    IsQuantity = stable > 0m, StableQuantity = stable,
                })).Id;

            case ProcessType.Scrap:
                return (await _scraps.CreateAsync(new ScrapCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = unit,
                    Factor = item.Factor ?? ScrapConsts.DefaultFactor,
                })).Id;

            case ProcessType.Future:
                return (await _futures.CreateAsync(new FutureCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = unit,
                    FollowingFactor = item.Factor ?? 1m,
                })).Id;

            case ProcessType.Jewelry:
                return (await _jewelries.CreateAsync(new JewelryCreateDto { Code = code, Name = name })).Id;

            case ProcessType.Stone:
                return (await _stones.CreateAsync(new StoneCreateDto { Code = code, Name = name })).Id;

            case ProcessType.Good:
            {
                // MAMUL URUNUN PROJEKSIYONUDUR (2026-08-10 Hakan): ciplak kod+ad ile acmak, urunun gorsellerini ve
                // varyantlarini KAYBEDIYORDU; ustelik varyant grafi bos gidince ana varyant "ANAVARYANT"
                // sentinel koduyla doguyordu. Projeksiyon ZATEN vardi (ProductToGoodProjector) ve dogru
                // davranisi biliyor - burada kullanilmiyordu.
                //
                // KOD/AD projeksiyondan DEGIL cagirandan gelir: kullanici sihirbazda degistirmis olabilir ve
                // kod benzersizlestirme dongusu (WithUniqueCodeAsync) bu degeri uretir.
                var projected = await _goodProjector.ProjectAsync(productId);
                return (await _goods.CreateAsync(new GoodCreateDto
                {
                    Code = code,
                    Name = name,
                    Attributes = projected.Attributes,
                    Variants = projected.Variants,
                    Media = projected.Media,
                })).Id;
            }

            case ProcessType.Service:
                return (await _services.CreateAsync(new ServiceCreateDto { Code = code, Name = name })).Id;

            default:
                throw new BusinessException("TradeXpress:Product:CommodityFamilyNotProvisionable")
                    .WithData("Family", item.Family.ToString());
        }
    }

    /// <summary>KLON'un aile dalı: kaynağı ailenin app service'iyle OKUR, alanlarını yeni kod/adla kopyalar.
    /// <para><b>Graf (varyant/belge/not/nitelik) KOPYALANMAZ</b> — bilinçli. Klonun amacı ölçü/ayar
    /// devralmaktır; kaynağın varyantlarını da taşımak, kullanıcının istemediği kayıtları sessizce
    /// çoğaltırdı. Varyantlar yeni emtianın kendi ekranında kurulur.</para></summary>
    private async Task<Guid?> CloneOfFamilyAsync(ProcessType family, Guid sourceId, string code, string name)
    {
        switch (family)
        {
            case ProcessType.Metal:
            {
                var s = await _metals.GetAsync(sourceId);
                return (await _metals.CreateAsync(new MetalCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = s.FollowingUnitId,
                    Factor = s.Factor, FactorChange = s.FactorChange,
                    IsQuantity = s.IsQuantity, StableQuantity = s.StableQuantity,
                    CostUnitId = s.CostUnitId, Description = s.Description,
                })).Id;
            }

            case ProcessType.Scrap:
            {
                var s = await _scraps.GetAsync(sourceId);
                return (await _scraps.CreateAsync(new ScrapCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = s.FollowingUnitId,
                    Factor = s.Factor, FactorChange = s.FactorChange, Description = s.Description,
                })).Id;
            }

            case ProcessType.Future:
            {
                var s = await _futures.GetAsync(sourceId);
                return (await _futures.CreateAsync(new FutureCreateDto
                {
                    Code = code, Name = name, FollowingUnitId = s.FollowingUnitId,
                    FollowingFactor = s.FollowingFactor, Description = s.Description,
                })).Id;
            }

            case ProcessType.Jewelry:
            {
                var s = await _jewelries.GetAsync(sourceId);
                return (await _jewelries.CreateAsync(new JewelryCreateDto
                {
                    Code = code, Name = name,
                    IsQuantity = s.IsQuantity, PriceByQuantity = s.PriceByQuantity,
                    PriceTypeChange = s.PriceTypeChange,
                    EntryPrice = s.EntryPrice, EntryPriceUnitId = s.EntryPriceUnitId,
                    ExitPrice = s.ExitPrice, ExitPriceUnitId = s.ExitPriceUnitId,
                    Description = s.Description,
                })).Id;
            }

            case ProcessType.Stone:
            {
                var s = await _stones.GetAsync(sourceId);
                return (await _stones.CreateAsync(new StoneCreateDto
                {
                    Code = code, Name = name,
                    IsQuantity = s.IsQuantity, PriceByQuantity = s.PriceByQuantity,
                    PriceTypeChange = s.PriceTypeChange,
                    EntryPrice = s.EntryPrice, EntryPriceUnitId = s.EntryPriceUnitId,
                    ExitPrice = s.ExitPrice, ExitPriceUnitId = s.ExitPriceUnitId,
                    Description = s.Description,
                })).Id;
            }

            case ProcessType.Good:
            {
                var s = await _goods.GetAsync(sourceId);
                return (await _goods.CreateAsync(new GoodCreateDto
                {
                    Code = code, Name = name,
                    IsQuantity = s.IsQuantity, PriceByQuantity = s.PriceByQuantity,
                    PriceTypeChange = s.PriceTypeChange, StockUnitCode = s.StockUnitCode,
                    VatPurchaseRate = s.VatPurchaseRate, VatSaleRate = s.VatSaleRate,
                    OtvRate = s.OtvRate, WithholdingRate = s.WithholdingRate,
                    Brand = s.Brand, Category = s.Category, Description = s.Description,
                })).Id;
            }

            case ProcessType.Service:
            {
                var s = await _services.GetAsync(sourceId);
                return (await _services.CreateAsync(new ServiceCreateDto
                {
                    Code = code, Name = name, Description = s.Description,
                })).Id;
            }

            default:
                throw new BusinessException("TradeXpress:Product:CommodityFamilyNotProvisionable")
                    .WithData("Family", family.ToString());
        }
    }
}
