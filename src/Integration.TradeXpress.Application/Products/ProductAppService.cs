using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Product CRUD — <b>company-owned + per-tenant</b> katalog (AssayOffice company-scope + Account graf-save deseni
/// birleşimi). Kapsam DAİMA çalışılan şirket (<see cref="ICurrentCompany"/>; sunucu zorlar — client CompanyId
/// GÖNDERMEZ). Kimlik (Code uppercase normalize, şirket-scope benzersizlik). Nitelikler + değerleri in-memory
/// grafla yönetilir (add/update/delete; ürün başına en fazla 5 — <see cref="ProductAttributeConsts"/>).
/// Varyantlar ELLE EKLENMEZ/SİLİNMEZ: nitelik×değer kartezyeninden <see cref="ProductVariantSynchronizer"/>
/// ÜRETİR (save sonunda); grafla yalnız mevcut varyant GÜNCELLENİR (Code/Name/Description/IsActive).
/// </summary>
[Authorize(TradeXpressPermissions.Products.Default)]
public class ProductAppService : TradeXpressAppService, IProductAppService
{
    private readonly IRepository<Product, Guid> _repository;
    private readonly IRepository<ProductVariant, Guid> _variantRepository;
    private readonly IRepository<ProductAttribute, Guid> _attributeRepository;
    private readonly IRepository<ProductAttributeValue, Guid> _valueRepository;
    private readonly IRepository<ProductVariantAttributeValue, Guid> _linkRepository;
    private readonly IRepository<ProductVariantRecipeLine, Guid> _recipeLineRepository;
    private readonly IRepository<Metal, Guid> _metalRepository;
    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly IRepository<Stone, Guid> _stoneRepository;
    private readonly ProductVariantManager _variantManager;
    private readonly ProductVariantSynchronizer _variantSynchronizer;
    private readonly IEffectivePriceAppService _effectivePriceAppService;
    private readonly ProductRecipeCostCalculator _recipeCostCalculator;
    private readonly ICurrentCompany _currentCompany;
    private readonly IBlobContainer<ProductImagesContainer> _imageContainer;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    public ProductAppService(
        IRepository<Product, Guid> repository,
        IRepository<ProductVariant, Guid> variantRepository,
        IRepository<ProductAttribute, Guid> attributeRepository,
        IRepository<ProductAttributeValue, Guid> valueRepository,
        IRepository<ProductVariantAttributeValue, Guid> linkRepository,
        IRepository<ProductVariantRecipeLine, Guid> recipeLineRepository,
        IRepository<Metal, Guid> metalRepository,
        IRepository<Jewelry, Guid> jewelryRepository,
        IRepository<Stone, Guid> stoneRepository,
        ProductVariantManager variantManager,
        ProductVariantSynchronizer variantSynchronizer,
        IEffectivePriceAppService effectivePriceAppService,
        ProductRecipeCostCalculator recipeCostCalculator,
        ICurrentCompany currentCompany,
        IBlobContainer<ProductImagesContainer> imageContainer)
    {
        _repository = repository;
        _variantRepository = variantRepository;
        _attributeRepository = attributeRepository;
        _valueRepository = valueRepository;
        _linkRepository = linkRepository;
        _recipeLineRepository = recipeLineRepository;
        _metalRepository = metalRepository;
        _jewelryRepository = jewelryRepository;
        _stoneRepository = stoneRepository;
        _variantManager = variantManager;
        _variantSynchronizer = variantSynchronizer;
        _effectivePriceAppService = effectivePriceAppService;
        _recipeCostCalculator = recipeCostCalculator;
        _currentCompany = currentCompany;
        _imageContainer = imageContainer;
    }

    public virtual async Task<PagedResultDto<ProductListDto>> GetListAsync(ProductListRequestDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            return new PagedResultDto<ProductListDto>(0, new List<ProductListDto>());

        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId)
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var counts = await LoadVariantCountsAsync(items.Select(p => p.Id));

        return new PagedResultDto<ProductListDto>(
            totalCount,
            items.Select(p => new ProductListDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                IsActive = p.IsActive,
                VariantCount = counts.GetValueOrDefault(p.Id),
            }).ToList());
    }

    public virtual async Task<ProductGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await _repository.GetAsync(id));

    [Authorize(TradeXpressPermissions.Products.Create)]
    public virtual async Task<ProductGetDto> CreateAsync(ProductCreateDto input)
    {
        if (_currentCompany.Id is not { } companyId)
            throw new BusinessException("TradeXpress:Product:CompanyRequired");

        // Benzersizlik ÖN-kontrolü (Update ile simetrik): aynı şirkette aynı kodlu ürün → dostane hata,
        // ham DB (TenantId, CompanyId, Code) unique çakışması değil. Kendisi yok → excludeId boş.
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Product.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(companyId, normalizedCode, Guid.Empty);

        var entity = new Product(companyId, input.Code, input.Name);
        entity.SetDescription(input.Description);
        entity.SetImages(MapImages(input.Images));
        await _repository.InsertAsync(entity, autoSave: true);

        var valueIdByClientKey = await SaveAttributesAsync(entity, input.Attributes);
        // DB mutabakatı (kartezyen üret/temizle) + en-az-1 + tekil-main garantisi; SONRA kullanıcının
        // kaydet-öncesi varyant özelleştirmeleri (Id ya da CombinationKey eşlemesiyle) uygulanır.
        await _variantSynchronizer.SynchronizeAsync(entity);
        await ApplyVariantCustomizationsAsync(entity, input.Variants, valueIdByClientKey);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Products.Update)]
    public virtual async Task<ProductGetDto> UpdateAsync(Guid id, ProductUpdateDto input)
    {
        var entity = await _repository.GetAsync(id);
        await ApplyCodeChangeAsync(entity, input.Code);
        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);
        var oldImages = entity.Images.ToList();   // yetim blob temizliği için değişim ÖNCESİ resim
        entity.SetImages(MapImages(input.Images));
        await DeleteOrphanImageBlobsAsync(oldImages, entity.Images);
        await _repository.UpdateAsync(entity, autoSave: true);

        var valueIdByClientKey = await SaveAttributesAsync(entity, input.Attributes);
        // DB mutabakatı (kartezyen üret/temizle) + en-az-1 + tekil-main garantisi; SONRA kullanıcının
        // kaydet-öncesi varyant özelleştirmeleri (Id ya da CombinationKey eşlemesiyle) uygulanır.
        await _variantSynchronizer.SynchronizeAsync(entity);
        await ApplyVariantCustomizationsAsync(entity, input.Variants, valueIdByClientKey);
        return await ToGetDtoAsync(entity);
    }

    /// <summary>Nitelik grafından varyant ÜRETİMİ — PERSISTSİZ önizleme (DB'ye yazmaz, kayıt gerekmez).
    /// Kartezyen + kod/ad türetme <see cref="ProductVariantSynchronizer"/> ile AYNI (public static helper'lar);
    /// ilk satır IsMain (display), hepsi aktif; <c>CombinationKey</c> = değer ClientKey'lerinin sıralı join'i
    /// (kayıtta özelleştirme eşlemesi için round-trip edilir).</summary>
    public virtual Task<List<ProductVariantGraphDto>> GenerateVariantsAsync(ProductVariantGenerateRequestDto input)
    {
        var result = new List<ProductVariantGraphDto>();
        var axes = BuildGenerationAxes(input.Attributes);
        if (axes.Count == 0)
        {
            return Task.FromResult(result);   // nitelik yok → üretilecek kombinasyon yok (base varyant save'de doğar)
        }

        foreach (var combination in BuildDtoCartesian(axes))
        {
            var valueNames = combination.Select(x => x.NormalizedValue).ToList();
            // Kombinasyon özeti "Nitelik: Değer" çiftleri (attribute DisplayOrder = eksen sırası), ", " join.
            var summary = string.Join(", ", combination.Select(x => $"{x.AttributeName}: {x.NormalizedValue}"));
            result.Add(new ProductVariantGraphDto
            {
                IsMain = result.Count == 0,   // display-only; kalıcı main garantisi manager/synchronizer'da
                Code = ProductVariantSynchronizer.BuildVariantCode(valueNames).ToUpperInvariant(),
                Name = ProductVariantSynchronizer.BuildVariantName(input.ProductName?.Trim() ?? string.Empty, valueNames).Trim(),
                IsActive = true,
                AttributeSummary = summary,
                CombinationKey = BuildCombinationKeyFromClientKeys(combination.Select(x => x.Value.ClientKey)),
            });
        }

        return Task.FromResult(result);
    }

    /// <summary>Reçete satırlarının CANLI maliyetini PERSISTSİZ hesaplar (tam kayıt gerekmez) — sanal varyant kurup
    /// GetAsync ile AYNI <see cref="PopulateRecipeCostsAsync"/> motorunu çağırır (ülke birimine rebase + calculator).
    /// Satırlar LineOrder sırasına dizilir (calculator ordinal + devreden bu sıraya dayanır). DB'ye YAZMAZ.</summary>
    public virtual async Task<ProductRecipeCostResultDto> CalculateRecipeCostAsync(ProductRecipeCostRequestDto input)
    {
        var ordered = (input?.Lines ?? new List<ProductRecipeLineGraphDto>())
            .Where(l => !l.IsDeleted)
            .OrderBy(l => l.LineOrder)
            .ToList();

        var variant = new ProductVariantGraphDto { RecipeLines = ordered };
        await PopulateRecipeCostsAsync(new List<ProductVariantGraphDto> { variant });

        return new ProductRecipeCostResultDto
        {
            NetCost = variant.NetCost,
            NetCostCurrency = variant.NetCostCurrency,
            NetCostMissingRate = variant.NetCostMissingRate,
            Lines = ordered,
        };
    }

    [Authorize(TradeXpressPermissions.Products.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // Güvenlik sınırı (Account deseni): ürünü ÖNCE yükle — company query filter yabancı şirketin ürününü
        // gizler → EntityNotFoundException. Doğrulama varyant silmeden ÖNCE olmalı.
        var entity = await _repository.GetAsync(id);
        await DeleteOrphanImageBlobsAsync(entity.Images, newImages: null);   // ürünle birlikte upload blobları da temizlenir
        await DeleteRecipeLinesOfProductAsync(entity.Id);
        await DeleteAttributeGraphOfProductAsync(entity.Id);
        await _variantManager.DeleteVariantsOfProductAsync(entity.Id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Ürünün TÜM varyantlarının reçete satırlarını siler — varyantlar silinmeden önce (orphan önleme).</summary>
    private async Task DeleteRecipeLinesOfProductAsync(Guid productId)
    {
        var variantIds = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == productId).Select(v => v.Id));
        if (variantIds.Count == 0)
        {
            return;
        }

        await _recipeLineRepository.DeleteAsync(r => variantIds.Contains(r.ProductVariantId), autoSave: true);
    }

    /// <summary>Ürünün nitelik grafını (bağ + değer + nitelik satırları) siler — ürün silinmeden önce.
    /// Her bağ (link) bu ürünün bir niteliğine işaret ettiğinden attribute-id kümesi tüm bağları kapsar.</summary>
    private async Task DeleteAttributeGraphOfProductAsync(Guid productId)
    {
        var attributeIds = await AsyncExecuter.ToListAsync(
            (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == productId).Select(a => a.Id));
        if (attributeIds.Count == 0)
        {
            return;
        }

        await _linkRepository.DeleteAsync(l => attributeIds.Contains(l.ProductAttributeId), autoSave: true);
        await _valueRepository.DeleteAsync(v => attributeIds.Contains(v.ProductAttributeId), autoSave: true);
        await _attributeRepository.DeleteAsync(a => a.ProductId == productId, autoSave: true);
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): normalize et → değiştiyse AYNI ŞİRKET altında
    /// benzersizliği doğrula (kendisi hariç; dostane hata) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Product entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(entity.CompanyId, normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği ((TenantId, CompanyId, Code) unique index'iyle hizalı).
    /// Create'te <paramref name="excludeId"/>=Guid.Empty, Update'te entity.Id. Dostane BusinessException.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(p => p.CompanyId == companyId && p.Id != excludeId && p.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Product:CodeAlreadyExists");
        }
    }

    // ── varyant grafı: YALNIZ ÖZELLEŞTİRME (attribute-driven kural) — varyantlar elle EKLENMEZ/SİLİNMEZ
    //    (synchronizer üretir/temizler; IsDeleted YOKSAYILIR). Senkron SONRASI çalışır: satır Id ile (mevcut)
    //    ya da CombinationKey ile (üretim önizlemesinden gelen, henüz Id'siz) DB varyantına eşlenir; kullanıcının
    //    kaydet-öncesi Code/Name/Description/IsActive dokunuşları (ör. pasife çekme) KAYBOLMAZ. ──
    private async Task ApplyVariantCustomizationsAsync(
        Product product,
        List<ProductVariantGraphDto> variants,
        Dictionary<Guid, Guid> valueIdByClientKey)
    {
        if (variants == null || variants.Count == 0) return;

        var dbVariants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == product.Id));
        var variantIds = dbVariants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<ProductVariantAttributeValue>()
            : await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));

        // Kombinasyon imzası (sıralı valueId join) → DB varyantı — synchronizer ile AYNI anahtar (DRY).
        var byCombination = dbVariants.ToDictionary(
            v => ProductVariantSynchronizer.BuildKey(
                links.Where(l => l.ProductVariantId == v.Id).Select(l => l.ProductAttributeValueId)),
            v => v);

        foreach (var v in variants)
        {
            var target = ResolveTargetVariant(v);
            if (target == null)
            {
                continue;   // senkronun sildiği / eşleşmeyen (bayat önizleme) satır → yoksay
            }

            await ApplyVariantFieldsAsync(product, target, v);
            await SaveRecipeLinesAsync(target, v.RecipeLines);
        }

        // Satırın hedef DB varyantı: (a) Id doluysa Id ile; (b) Id boşsa CombinationKey'in değer
        // ClientKey'leri persist eşlemesinden ValueId'lere çevrilir → aynı kombinasyonlu varyant.
        ProductVariant? ResolveTargetVariant(ProductVariantGraphDto dto)
        {
            if (dto.Id != Guid.Empty)
            {
                return dbVariants.FirstOrDefault(x => x.Id == dto.Id);
            }

            // Yeni ürünün seed'lenmiş base main'i (Id yok, IsMain, kombinasyon yok) → server'ın yarattığı DB main'e
            // eşle → Yeni'de girilen reçete/özelleştirme ana varyanta yazılır (ANAVARYANT set = server ile aynı).
            if (dto.IsMain && string.IsNullOrEmpty(dto.CombinationKey))
            {
                return dbVariants.FirstOrDefault(x => x.IsMain);
            }

            if (string.IsNullOrEmpty(dto.CombinationKey))
            {
                return null;
            }

            var valueIds = new List<Guid>();
            foreach (var part in dto.CombinationKey.Split('|'))
            {
                if (!Guid.TryParse(part, out var clientKey) || !valueIdByClientKey.TryGetValue(clientKey, out var valueId))
                {
                    return null;   // değer bu kayıtta persist edilmedi (silinmiş/bayat) → eşleşme yok
                }

                valueIds.Add(valueId);
            }

            return byCombination.GetValueOrDefault(ProductVariantSynchronizer.BuildKey(valueIds));
        }
    }

    /// <summary>Kullanıcı özelleştirmelerini varyanta uygular — ürün-scope kod benzersizliği korunur;
    /// IsMain'e DOKUNULMAZ (display-only; değişmez manager'da).</summary>
    private async Task ApplyVariantFieldsAsync(Product product, ProductVariant variant, ProductVariantGraphDto v)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            v.Code, nameof(ProductVariant.Code), EntityFieldConsts.CodeMinLength, ProductConsts.CodeMaxLength);
        if (!string.Equals(normalizedCode, variant.Code, StringComparison.Ordinal))
        {
            await EnsureVariantCodeUniqueAsync(product.Id, normalizedCode, variant.Id);
            variant.SetCode(normalizedCode);
        }

        variant.SetName(v.Name);
        variant.SetDescription(v.Description);
        variant.SetActive(v.IsActive);
        variant.SetSalePrice(v.SalePrice, v.SalePriceCurrencyUnitId);
        variant.SetStock(v.StockQuantity);
        variant.SetTradeIdentifiers(v.Barcode, v.Gtin, v.Mpn, v.Oem);
        await _variantRepository.UpdateAsync(variant, autoSave: true);
    }

    /// <summary>Aynı ÜRÜN altında varyant Code benzersizliği. Dostane BusinessException — ham DB çakışmasını önler.</summary>
    private async Task EnsureVariantCodeUniqueAsync(Guid productId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(x => x.ProductId == productId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:ProductVariant:CodeAlreadyExists");
        }
    }

    // ── reçete grafı (varyant-scope; Id + IsDeleted diff, Account/SubAccount deseni). Bileşen türü set-once
    //    (toolbar tip belirler); LineOrder korunur. Company varyanttan denormalize. ──
    private async Task SaveRecipeLinesAsync(ProductVariant variant, List<ProductRecipeLineGraphDto> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return;
        }

        foreach (var l in lines.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _recipeLineRepository.DeleteAsync(l.Id, autoSave: true);
        }

        // Kalanları client sırasında (LineOrder) sırala + 0..n-1 YENİDEN NUMARALA → benzersiz/deterministik pozisyon.
        // Türev satırın "yalnız üsttekiler" referans filtresi + calculator ordinal'i bu sıraya dayanır.
        var survivors = lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineOrder).ToList();
        for (var i = 0; i < survivors.Count; i++)
        {
            survivors[i].LineOrder = i;
        }

        ValidateDerivedReferences(survivors);

        // 1. geçiş: TÜM satırları insert/update (skaler alanlar; türev SelectedLines kaynakları HARİÇ) →
        // ClientKey→Id (+ ClientKey→entity) sözlükleri (SaveAttributesAsync valueIdByClientKey deseni).
        var idByClientKey = new Dictionary<Guid, Guid>();
        var entityByClientKey = new Dictionary<Guid, ProductVariantRecipeLine>();
        foreach (var l in survivors)
        {
            ProductVariantRecipeLine entity;
            if (l.Id == Guid.Empty)
            {
                entity = new ProductVariantRecipeLine(variant.CompanyId, variant.Id, l.ComponentType, l.LineOrder);
                ApplyRecipeLineFields(entity, l);
                await _recipeLineRepository.InsertAsync(entity, autoSave: true);
                l.Id = entity.Id;
            }
            else
            {
                entity = await _recipeLineRepository.GetAsync(l.Id);
                entity.SetOrder(l.LineOrder);
                ApplyRecipeLineFields(entity, l);
                await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
            }

            idByClientKey[l.ClientKey] = l.Id;
            entityByClientKey[l.ClientKey] = entity;
        }

        // 2. geçiş: türev SelectedLines satırlarının kaynak ClientKey'lerini çözülmüş Id CSV'sine çevir + persist
        // (kaynak Id'ler artık 1. geçişten hazır). AllAbove satırlarının kaynağı yok (SetDerived null'a düşürdü).
        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            var csv = string.Join('|', l.DerivedSourceKeys.Select(k => idByClientKey[k].ToString()));
            var entity = entityByClientKey[l.ClientKey];
            entity.SetDerivedSources(csv);
            await _recipeLineRepository.UpdateAsync(entity, autoSave: true);
        }
    }

    /// <summary>Türev satır referans-bütünlüğü (kaydetmeden ÖNCE, fail-fast): SelectedLines satırının seçili
    /// kaynakları BOŞ olamaz, hepsi mevcut (silinmemiş) KARDEŞ satır olmalı ve yalnız kendinden ÖNCEKİ satırları
    /// (küçük LineOrder) referanslamalı → döngüsüz + kendine-referans yok. AllAbove kaynak gerektirmez.
    /// <paramref name="survivors"/>'ın LineOrder'ı 0..n-1 yeniden-numaralı (benzersiz pozisyon).</summary>
    private static void ValidateDerivedReferences(List<ProductRecipeLineGraphDto> survivors)
    {
        var orderByClientKey = survivors.ToDictionary(x => x.ClientKey, x => x.LineOrder);

        foreach (var l in survivors.Where(x => x.ComponentType == RecipeComponentType.Service
            && x.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines))
        {
            if (l.DerivedSourceKeys == null || l.DerivedSourceKeys.Count == 0)
            {
                throw new BusinessException("TradeXpress:ProductRecipeLine:DerivedNeedsSelection");
            }

            foreach (var key in l.DerivedSourceKeys)
            {
                if (!orderByClientKey.TryGetValue(key, out var sourceOrder) || sourceOrder >= l.LineOrder)
                {
                    // kaynak yok (silinmiş/yabancı) YA DA kendini/sonrasını referanslıyor → döngü/geçersiz.
                    throw new BusinessException("TradeXpress:ProductRecipeLine:DerivedRefMustBeUpstream");
                }
            }
        }
    }

    /// <summary>Graf düğümünün alanlarını reçete satırına uygular — bileşen türüne göre katalog-emtia ya da
    /// hizmet/manuel setter grubu. ComponentType set-once olduğundan burada DEĞİŞTİRİLMEZ (ctor'da atanır).</summary>
    private static void ApplyRecipeLineFields(ProductVariantRecipeLine entity, ProductRecipeLineGraphDto l)
    {
        if (l.ComponentType == RecipeComponentType.CatalogCommodity)
        {
            entity.SetCatalogCommodity(
                l.CommodityProcessType.GetValueOrDefault(),
                l.CommodityId,
                l.Quantity,
                l.Amount,
                l.Factor,
                l.ValuationUnitId,
                l.PaymentType,
                l.PayFactor,
                l.PayUnitId);
        }
        else
        {
            // Hizmet satırı: hizmet referansı (etiket) + türevsel bedel kuralı (taban modu + işlem + operand);
            // SelectedLines kaynakları AYRICA 2. geçişte SetDerivedSources ile (Id'ler o aşamada çözülür).
            entity.SetService(
                l.CommodityId,
                l.DerivedBaseMode.GetValueOrDefault(RecipeDerivedBaseMode.AllAbove),
                l.DerivedOperation.GetValueOrDefault(RecipeDerivedOperation.Percent),
                l.DerivedOperand,
                l.PayUnitId);
        }

        entity.SetDescription(l.Description);
    }

    // ── nitelik grafı diff (Id + IsDeleted) — değerler nitelik başına iç graf. DÖNÜŞ: değer ClientKey →
    //    persist-edilen ValueId eşlemesi (CombinationKey'li varyant özelleştirmelerinin çözümü için). ──
    private async Task<Dictionary<Guid, Guid>> SaveAttributesAsync(Product product, List<ProductAttributeGraphDto> attributes)
    {
        var valueIdByClientKey = new Dictionary<Guid, Guid>();
        if (attributes == null) return valueIdByClientKey;

        // Önce silinenler: nitelik + TÜM değer satırları. Bağ (link) satırlarına dokunulmaz —
        // sonda çalışan synchronizer kalkan kombinasyonların varyant+bağlarını zaten temizler.
        foreach (var a in attributes.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v => v.ProductAttributeId == a.Id, autoSave: true);
            await _attributeRepository.DeleteAsync(a.Id, autoSave: true);
        }

        // Graf ürünün TAM nitelik resmi (GetAsync hepsini döner) → max-5 + ad benzersizliği girdi üzerinde.
        var survivors = attributes.Where(x => !x.IsDeleted).ToList();
        if (survivors.Count > ProductAttributeConsts.MaxAttributesPerProduct)
        {
            throw new BusinessException("TradeXpress:Product:TooManyAttributes");
        }

        EnsureAttributeNamesUnique(survivors);
        EnsureEveryAttributeHasValue(survivors);

        foreach (var a in survivors)
        {
            if (a.Id == Guid.Empty)
            {
                var attribute = new ProductAttribute(product.CompanyId, product.Id, a.Name, a.DisplayOrder);
                await _attributeRepository.InsertAsync(attribute, autoSave: true);
                a.Id = attribute.Id;   // değer grafı yeni niteliğe bağlanabilsin
            }
            else
            {
                var attribute = await _attributeRepository.GetAsync(a.Id);
                attribute.SetName(a.Name);
                attribute.SetDisplayOrder(a.DisplayOrder);
                await _attributeRepository.UpdateAsync(attribute, autoSave: true);
            }

            await SaveAttributeValuesAsync(product, a, valueIdByClientKey);
        }

        return valueIdByClientKey;
    }

    /// <summary>Her (silinmemiş) nitelik en az bir (silinmemiş) değer içermeli — değersiz nitelik kaydedilemez;
    /// üretim (GenerateVariants) tarafıyla AYNI kural. Synchronizer'daki değersiz-eksen dalı savunma olarak kalır.</summary>
    private static void EnsureEveryAttributeHasValue(List<ProductAttributeGraphDto> survivors)
    {
        var hasEmptyAttribute = survivors.Any(a => a.Values == null || a.Values.All(v => v.IsDeleted));
        if (hasEmptyAttribute)
        {
            throw new BusinessException("TradeXpress:ProductAttribute:ValueRequired");
        }
    }

    /// <summary>Aynı üründe aynı adlı iki nitelik olamaz — normalize (TitleCase) adlar üzerinden dostane hata.</summary>
    private static void EnsureAttributeNamesUnique(List<ProductAttributeGraphDto> survivors)
    {
        var names = survivors.Select(a => StringFieldGuard.NormalizeName(
            a.Name, nameof(ProductAttribute.Name), EntityFieldConsts.NameMinLength, ProductAttributeConsts.NameMaxLength));
        if (HasDuplicate(names))
        {
            throw new BusinessException("TradeXpress:ProductAttribute:NameAlreadyExists");
        }
    }

    // ── değer grafı diff — nitelik başına (parent attribute Id'si SaveAttributesAsync'te garanti dolu).
    //    Persist edilen her değer için ClientKey→ValueId eşlemesi doldurulur (CombinationKey çözümü). ──
    private async Task SaveAttributeValuesAsync(
        Product product,
        ProductAttributeGraphDto attribute,
        Dictionary<Guid, Guid> valueIdByClientKey)
    {
        if (attribute.Values == null) return;

        foreach (var v in attribute.Values.Where(x => x.IsDeleted && x.Id != Guid.Empty))
        {
            await _valueRepository.DeleteAsync(v.Id, autoSave: true);
        }

        var survivors = attribute.Values.Where(x => !x.IsDeleted).ToList();
        EnsureAttributeValuesUnique(survivors);

        foreach (var v in survivors)
        {
            if (v.Id == Guid.Empty)
            {
                var value = new ProductAttributeValue(product.CompanyId, attribute.Id, v.Value, v.DisplayOrder);
                await _valueRepository.InsertAsync(value, autoSave: true);
                v.Id = value.Id;
            }
            else
            {
                var value = await _valueRepository.GetAsync(v.Id);
                value.SetValue(v.Value);
                value.SetDisplayOrder(v.DisplayOrder);
                await _valueRepository.UpdateAsync(value, autoSave: true);
            }

            valueIdByClientKey[v.ClientKey] = v.Id;
        }
    }

    /// <summary>Aynı nitelikte aynı değer iki kez olamaz — normalize değerler üzerinden dostane hata.</summary>
    private static void EnsureAttributeValuesUnique(List<ProductAttributeValueGraphDto> survivors)
    {
        var values = survivors.Select(v => StringFieldGuard.NormalizeName(
            v.Value, nameof(ProductAttributeValue.Value), EntityFieldConsts.NameMinLength, ProductAttributeConsts.ValueMaxLength));
        if (HasDuplicate(values))
        {
            throw new BusinessException("TradeXpress:ProductAttributeValue:ValueAlreadyExists");
        }
    }

    private static bool HasDuplicate(IEnumerable<string> normalized)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return normalized.Any(n => !seen.Add(n));
    }

    // ── persistsiz üretim (GenerateVariants) yardımcıları — sıralama/türetme synchronizer paritesinde ──

    /// <summary>Üretim eksenleri: silinmemiş nitelikler (DisplayOrder→Name) × silinmemiş, NORMALİZE değerler
    /// (DisplayOrder→Value) — synchronizer'ın entity sıralamasıyla AYNI. Her öğe niteliğin NORMALİZE adını da
    /// taşır (kombinasyon özeti "Nitelik: Değer" için). Değersiz nitelik → dostane hata
    /// (kayıt tarafındaki <see cref="EnsureEveryAttributeHasValue"/> ile aynı kural).</summary>
    private static List<List<GenerationAxisItem>> BuildGenerationAxes(List<ProductAttributeGraphDto> attributes)
    {
        var survivors = (attributes ?? new List<ProductAttributeGraphDto>())
            .Where(a => !a.IsDeleted)
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var axes = new List<List<GenerationAxisItem>>();
        foreach (var attribute in survivors)
        {
            var attributeName = StringFieldGuard.NormalizeName(
                attribute.Name, nameof(ProductAttribute.Name), EntityFieldConsts.NameMinLength, ProductAttributeConsts.NameMaxLength);

            var values = (attribute.Values ?? new List<ProductAttributeValueGraphDto>())
                .Where(v => !v.IsDeleted)
                .Select(v => new GenerationAxisItem(
                    v,
                    StringFieldGuard.NormalizeName(
                        v.Value, nameof(ProductAttributeValue.Value), EntityFieldConsts.NameMinLength, ProductAttributeConsts.ValueMaxLength),
                    attributeName))
                .OrderBy(x => x.Value.DisplayOrder).ThenBy(x => x.NormalizedValue)
                .ToList();

            if (values.Count == 0)
            {
                throw new BusinessException("TradeXpress:ProductAttribute:ValueRequired");
            }

            axes.Add(values);
        }

        return axes;
    }

    /// <summary>DTO kartezyeni — her eksenden bir değer (synchronizer BuildCartesian'ın DTO karşılığı).</summary>
    private static List<List<GenerationAxisItem>> BuildDtoCartesian(List<List<GenerationAxisItem>> axes)
    {
        var result = new List<List<GenerationAxisItem>> { new() };
        foreach (var axis in axes)
        {
            result = result
                .SelectMany(prefix => axis.Select(v =>
                {
                    var next = new List<GenerationAxisItem>(prefix) { v };
                    return next;
                }))
                .ToList();
        }

        return result;
    }

    /// <summary>Üretim ekseninin bir öğesi — değer DTO'su + normalize değer + normalize nitelik adı
    /// (kombinasyon özeti "Nitelik: Değer" için gerekli).</summary>
    private sealed record GenerationAxisItem(ProductAttributeValueGraphDto Value, string NormalizedValue, string AttributeName);

    /// <summary>Kombinasyonun istemci-taraflı kimliği — değer ClientKey'lerinin SIRALI "|" join'i
    /// (synchronizer BuildKey ile aynı biçim; sunucu üretir, client round-trip eder).</summary>
    private static string BuildCombinationKeyFromClientKeys(IEnumerable<Guid> clientKeys)
    {
        return string.Join("|", clientKeys.OrderBy(k => k));
    }

    /// <summary>Görsel graf düğümlerini owned tiplere çevirir (normalize/kırpma entity SetImages'ta).</summary>
    private static List<ProductImage> MapImages(List<ProductImageGraphDto> images)
    {
        return (images ?? new List<ProductImageGraphDto>())
            .Select(i => new ProductImage(i.SourceType, i.Url, i.BlobName, i.FileName, i.DisplayOrder, i.IsDefault))
            .ToList();
    }

    /// <summary>Blob (Upload) görsellerin önizleme data-URL'lerini doldurur — HEP küçük THUMBNAIL blobundan
    /// (tam içerik DTO'ya gömülmez; review'da kanıtlanan 4MB×8 şişmesi + dirty-check maliyeti). Thumbnail
    /// bulunamazsa önizleme boş kalır (fail-open; kayıt görünmeye devam eder).</summary>
    private async Task PopulateImagePreviewsAsync(List<ProductImageGraphDto> images)
    {
        foreach (var image in images.Where(i =>
            i.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(i.BlobName)))
        {
            var thumbnail = await _imageContainer.GetAllBytesOrNullAsync(
                ProductImageAppService.ThumbnailNameOf(image.BlobName!));
            if (thumbnail is not null)
            {
                image.PreviewDataUrl = ProductImageAppService.BuildPreviewDataUrl(thumbnail);
            }
        }
    }

    /// <summary>Artık referans edilmeyen upload bloblarını (ana + thumbnail) siler — görsel silme/değiştirme
    /// update'inde eski blob AppBlobs'ta yetim kalmasın (review bulgusu). Form iptaliyle yetim kalan
    /// (hiç kaydedilmemiş) upload'lar burada YAKALANMAZ — ileride süpürücü işi (bilinçli kabul).</summary>
    private async Task DeleteOrphanImageBlobsAsync(IEnumerable<ProductImage> oldImages, IEnumerable<ProductImage>? newImages)
    {
        var keep = new HashSet<string>(
            (newImages ?? Enumerable.Empty<ProductImage>())
                .Where(i => !string.IsNullOrEmpty(i.BlobName))
                .Select(i => i.BlobName!),
            StringComparer.Ordinal);

        foreach (var image in oldImages.Where(i =>
            i.SourceType == ProductImageSourceType.Upload
            && !string.IsNullOrEmpty(i.BlobName)
            && !keep.Contains(i.BlobName!)))
        {
            await _imageContainer.DeleteAsync(image.BlobName!);
            await _imageContainer.DeleteAsync(ProductImageAppService.ThumbnailNameOf(image.BlobName!));
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, int>> LoadVariantCountsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, int>();

        var grouped = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => ids.Contains(v.ProductId))
                .GroupBy(v => v.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() }));
        return grouped.ToDictionary(x => x.ProductId, x => x.Count);
    }

    private async Task<ProductGetDto> ToGetDtoAsync(Product p)
    {
        // Company filtresi AÇIK kalır (mevcut desen): tüm alt kayıtlar üründen denormalize aynı şirkette,
        // çalışılan şirket de ürünü görünür kılan şirket → ek Disable gerekmez (varyant sorgusuyla simetrik).
        var variants = await AsyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync()).Where(v => v.ProductId == p.Id).OrderBy(v => v.Code));

        var attributes = (await AsyncExecuter.ToListAsync(
                (await _attributeRepository.GetQueryableAsync()).Where(a => a.ProductId == p.Id)))
            .OrderBy(a => a.DisplayOrder).ThenBy(a => a.Name)
            .ToList();

        var attributeIds = attributes.Select(a => a.Id).ToList();
        var values = attributeIds.Count == 0
            ? new List<ProductAttributeValue>()
            : (await AsyncExecuter.ToListAsync(
                    (await _valueRepository.GetQueryableAsync()).Where(v => attributeIds.Contains(v.ProductAttributeId))))
                .OrderBy(v => v.DisplayOrder).ThenBy(v => v.Value)
                .ToList();

        var variantIds = variants.Select(v => v.Id).ToList();
        var links = variantIds.Count == 0
            ? new List<ProductVariantAttributeValue>()
            : await AsyncExecuter.ToListAsync(
                (await _linkRepository.GetQueryableAsync()).Where(l => variantIds.Contains(l.ProductVariantId)));

        // Reçete satırları (tüm varyantlar) — LineOrder sırasıyla.
        var recipeLines = variantIds.Count == 0
            ? new List<ProductVariantRecipeLine>()
            : (await AsyncExecuter.ToListAsync(
                    (await _recipeLineRepository.GetQueryableAsync()).Where(r => variantIds.Contains(r.ProductVariantId))))
                .OrderBy(r => r.LineOrder).ThenBy(r => r.CreationTime)
                .ToList();

        var variantDtos = variants.Select(v => new ProductVariantGraphDto
        {
            Id = v.Id,
            IsMain = v.IsMain,
            Code = v.Code,
            Name = v.Name,
            Description = v.Description,
            IsActive = v.IsActive,
            SalePrice = v.SalePrice,
            SalePriceCurrencyUnitId = v.SalePriceCurrencyUnitId,
            StockQuantity = v.StockQuantity,
            Barcode = v.Barcode,
            Gtin = v.Gtin,
            Mpn = v.Mpn,
            Oem = v.Oem,
            AttributeSummary = BuildAttributeSummary(v.Id, attributes, values, links),
            RecipeLines = recipeLines
                .Where(r => r.ProductVariantId == v.Id)
                .Select(r => new ProductRecipeLineGraphDto
                {
                    Id = r.Id,
                    LineOrder = r.LineOrder,
                    ComponentType = r.ComponentType,
                    CommodityProcessType = r.CommodityProcessType,
                    CommodityId = r.CommodityId,
                    Quantity = r.Quantity,
                    Amount = r.Amount,
                    Factor = r.Factor,
                    ValuationUnitId = r.ValuationUnitId,
                    PaymentType = r.PaymentType,
                    PayFactor = r.PayFactor,
                    PayUnitId = r.PayUnitId,
                    ManualAmount = r.ManualAmount ?? 0m,
                    ManualUnitId = r.ManualUnitId,
                    Description = r.Description,
                    DerivedBaseMode = r.DerivedBaseMode,
                    DerivedOperation = r.DerivedOperation,
                    DerivedOperand = r.DerivedOperand,
                }).ToList(),
        }).ToList();

        // Türev SelectedLines kaynak Id'lerini bu oturumun taze ClientKey'lerine çevir (UI round-trip) — CANLI hesaptan ÖNCE.
        ResolveDerivedSourceKeys(variantDtos, recipeLines);

        // CANLI net maliyet — değerleme dict'i ÜRÜN başına BİR KEZ çekilir, tüm varyant/satırlarda yeniden kullanılır.
        await PopulateRecipeCostsAsync(variantDtos);

        var imageDtos = p.Images.Select(i => new ProductImageGraphDto
        {
            SourceType = i.SourceType,
            Url = i.Url,
            BlobName = i.BlobName,
            FileName = i.FileName,
            DisplayOrder = i.DisplayOrder,
            IsDefault = i.IsDefault,
        }).ToList();
        await PopulateImagePreviewsAsync(imageDtos);

        return new ProductGetDto
        {
            Id = p.Id,
            Code = p.Code,
            Name = p.Name,
            Description = p.Description,
            IsActive = p.IsActive,
            Images = imageDtos,
            Attributes = attributes.Select(a => new ProductAttributeGraphDto
            {
                Id = a.Id,
                Name = a.Name,
                DisplayOrder = a.DisplayOrder,
                Values = values.Where(v => v.ProductAttributeId == a.Id)
                    .Select(v => new ProductAttributeValueGraphDto
                    {
                        Id = v.Id,
                        Value = v.Value,
                        DisplayOrder = v.DisplayOrder,
                    }).ToList(),
            }).ToList(),
            Variants = variantDtos,
        };
    }

    /// <summary>Türev SelectedLines satırlarının persist edilmiş kaynak-Id CSV'sini, bu oturumda üretilmiş taze
    /// ClientKey'lere çevirir (UI round-trip + canlı hesap ordinal çözümü için). Kaydetme referans-bütünlüğü
    /// sağladığından Id'ler kardeş satırlara çözülür; çözülemeyen (teorik) parça sessizce atlanır.</summary>
    private static void ResolveDerivedSourceKeys(
        List<ProductVariantGraphDto> variants, List<ProductVariantRecipeLine> entities)
    {
        var sourceCsvById = entities
            .Where(e => e.ComponentType == RecipeComponentType.Service && !string.IsNullOrEmpty(e.DerivedSourceLineIds))
            .ToDictionary(e => e.Id, e => e.DerivedSourceLineIds!);
        if (sourceCsvById.Count == 0)
        {
            return;
        }

        foreach (var variant in variants)
        {
            var clientKeyById = variant.RecipeLines.ToDictionary(l => l.Id, l => l.ClientKey);
            foreach (var l in variant.RecipeLines.Where(x => x.ComponentType == RecipeComponentType.Service))
            {
                if (!sourceCsvById.TryGetValue(l.Id, out var csv))
                {
                    continue;
                }

                l.DerivedSourceKeys = csv
                    .Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => Guid.TryParse(part, out var srcId) && clientKeyById.TryGetValue(srcId, out var ck)
                        ? ck
                        : (Guid?)null)
                    .Where(ck => ck.HasValue)
                    .Select(ck => ck!.Value)
                    .ToList();
            }
        }
    }

    // ── CANLI reçete maliyeti (design-time; ledger'a YAZMAZ) ─────────────────────────────────────────
    // Değerleme dict'i (ülke birimine rebase, SELL bacağı) + katalog canlı verisi ÜRÜN başına BİR KEZ; tüm
    // varyant/satırlarda yeniden kullanılır (perf). Ülke birimi çözülemezse NetCost boş bırakılır.
    private async Task PopulateRecipeCostsAsync(List<ProductVariantGraphDto> variants)
    {
        var allLines = variants.SelectMany(v => v.RecipeLines).ToList();
        if (allLines.Count == 0)
        {
            return;
        }

        var countryUnitId = await _effectivePriceAppService.GetWorkingLocalCurrencyUnitIdAsync();
        if (countryUnitId is not { } targetUnitId)
        {
            return;   // ülke (rebase hedefi) birimi yok → net maliyet hesaplanamaz (boş)
        }

        // Değerleme: ülke birimine rebase'li efektifler (SELL bacağı — reçete kararı). ÜRÜN başına TEK çağrı.
        var valuation = await _effectivePriceAppService.GetValuationByBaseAsync(targetUnitId);
        var sellByUnit = valuation.ToDictionary(v => v.Id, v => v.Sell);
        var codeByUnit = valuation.ToDictionary(v => v.Id, v => v.CurrencyUnitCode);
        var countryCode = valuation.FirstOrDefault()?.BaseCurrencyCode ?? string.Empty;

        var catalog = await LoadRecipeCatalogAsync(allLines);

        foreach (var variant in variants)
        {
            variant.NetCostCurrency = countryCode;
            if (variant.RecipeLines.Count == 0)
            {
                continue;
            }

            // Türev SelectedLines ordinal çözümü için ClientKey→pozisyon (satırlar LineOrder sırasında).
            var ordinalByClientKey = new Dictionary<Guid, int>();
            for (var idx = 0; idx < variant.RecipeLines.Count; idx++)
            {
                ordinalByClientKey[variant.RecipeLines[idx].ClientKey] = idx;
            }

            var inputs = variant.RecipeLines.Select(l => BuildCostInput(l, catalog, ordinalByClientKey)).ToList();
            var result = _recipeCostCalculator.Compute(inputs, sellByUnit, countryCode);

            for (var i = 0; i < variant.RecipeLines.Count; i++)
            {
                var line = variant.RecipeLines[i];
                var r = result.Lines[i];
                line.LineCost = r.Cost;
                line.LineCostMissingRate = r.MissingRate;
                line.Total = r.Total;
                line.PayTotal = r.PayTotal;
                line.AppliedBase = r.AppliedBase;
                line.RunningSubtotal = r.RunningSubtotal;
                line.MainUnitCode = line.ValuationUnitId is { } mu ? codeByUnit.GetValueOrDefault(mu, string.Empty) : string.Empty;
                line.PayUnitCode = line.PayUnitId is { } pu ? codeByUnit.GetValueOrDefault(pu, string.Empty) : string.Empty;
            }

            variant.NetCost = result.Net;
            variant.NetCostMissingRate = result.AnyMissingRate;
        }
    }

    /// <summary>Graf düğümünden calculator girdisi kurar — katalog canlı verisi (metal adet→gram, parasal giriş
    /// fiyatı) <paramref name="catalog"/>'dan çözülür; eksikse 0 (satır sonra MissingRate/0 verir).</summary>
    private static RecipeLineCostInput BuildCostInput(
        ProductRecipeLineGraphDto l, RecipeCatalogData catalog, Dictionary<Guid, int> ordinalByClientKey)
    {
        var isQuantity = false;
        var stableQuantity = 0m;
        var priceByQuantity = false;
        var entryPrice = 0m;
        var laborByQuantity = false;

        if (l.ComponentType == RecipeComponentType.CatalogCommodity && l.CommodityId is { } commodityId)
        {
            if (l.CommodityProcessType == ProcessType.Metal && catalog.Metals.TryGetValue(commodityId, out var m))
            {
                isQuantity = m.IsQuantity;
                stableQuantity = m.StableQuantity;
                laborByQuantity = m.LaborByQuantity;
            }
            else if (l.CommodityProcessType == ProcessType.Jewelry && catalog.Jewelries.TryGetValue(commodityId, out var j))
            {
                entryPrice = j.EntryPrice;
                priceByQuantity = j.PriceByQuantity;
            }
            else if (l.CommodityProcessType == ProcessType.Stone && catalog.Stones.TryGetValue(commodityId, out var s))
            {
                entryPrice = s.EntryPrice;
                priceByQuantity = s.PriceByQuantity;
            }
        }

        // Türev SelectedLines: seçili kaynak ClientKey'leri → pozisyon ordinal'leri (calculator upstream doğrular).
        var derivedOrdinals = l.ComponentType == RecipeComponentType.Service
            && l.DerivedBaseMode == RecipeDerivedBaseMode.SelectedLines
            ? l.DerivedSourceKeys.Where(ordinalByClientKey.ContainsKey).Select(k => ordinalByClientKey[k]).ToList()
            : new List<int>();

        return new RecipeLineCostInput(
            l.ComponentType,
            l.CommodityProcessType,
            l.Quantity,
            l.Amount,
            l.Factor,
            isQuantity,
            stableQuantity,
            priceByQuantity,
            entryPrice,
            l.ValuationUnitId,
            l.PaymentType,
            l.PayFactor,
            l.PayUnitId,
            laborByQuantity,
            l.ManualAmount,
            l.ManualUnitId,
            l.DerivedBaseMode,
            l.DerivedOperation,
            l.DerivedOperand,
            derivedOrdinals);
    }

    /// <summary>Reçetede geçen katalog kayıtlarının hesaba giren canlı verisini (metal adet→gram; parasal giriş
    /// fiyatı) TEK batch'te yükler. Filtreler kapalı (host/global katalog kaydı da çözülsün — salt-okuma).</summary>
    private async Task<RecipeCatalogData> LoadRecipeCatalogAsync(List<ProductRecipeLineGraphDto> lines)
    {
        Guid[] IdsOfFamily(ProcessType family)
        {
            return lines
                .Where(l => l.ComponentType == RecipeComponentType.CatalogCommodity
                    && l.CommodityProcessType == family
                    && l.CommodityId is not null)
                .Select(l => l.CommodityId!.Value)
                .Distinct()
                .ToArray();
        }

        var metalIds = IdsOfFamily(ProcessType.Metal);
        var jewelryIds = IdsOfFamily(ProcessType.Jewelry);
        var stoneIds = IdsOfFamily(ProcessType.Stone);

        var metals = new Dictionary<Guid, MetalCatalogCost>();
        var jewelries = new Dictionary<Guid, PricedCatalogCost>();
        var stones = new Dictionary<Guid, PricedCatalogCost>();

        using (DataFilter.Disable<IMultiTenant>())
        using (DataFilter.Disable<ICompanyScoped>())
        {
            if (metalIds.Length > 0)
            {
                metals = (await AsyncExecuter.ToListAsync(
                        (await _metalRepository.GetQueryableAsync()).Where(m => metalIds.Contains(m.Id))))
                    .ToDictionary(m => m.Id, m => new MetalCatalogCost(
                        m.IsQuantity, m.StableQuantity, m.LaborType == MetalLaborType.Quantity));
            }

            if (jewelryIds.Length > 0)
            {
                jewelries = (await AsyncExecuter.ToListAsync(
                        (await _jewelryRepository.GetQueryableAsync()).Where(j => jewelryIds.Contains(j.Id))))
                    .ToDictionary(j => j.Id, j => new PricedCatalogCost(j.EntryPrice, j.PriceByQuantity));
            }

            if (stoneIds.Length > 0)
            {
                stones = (await AsyncExecuter.ToListAsync(
                        (await _stoneRepository.GetQueryableAsync()).Where(s => stoneIds.Contains(s.Id))))
                    .ToDictionary(s => s.Id, s => new PricedCatalogCost(s.EntryPrice, s.PriceByQuantity));
            }
        }

        return new RecipeCatalogData(metals, jewelries, stones);
    }

    private sealed record MetalCatalogCost(bool IsQuantity, decimal StableQuantity, bool LaborByQuantity);
    private sealed record PricedCatalogCost(decimal EntryPrice, bool PriceByQuantity);
    private sealed record RecipeCatalogData(
        Dictionary<Guid, MetalCatalogCost> Metals,
        Dictionary<Guid, PricedCatalogCost> Jewelries,
        Dictionary<Guid, PricedCatalogCost> Stones);

    /// <summary>Varyantın kombinasyon özeti — bağlı "Nitelik: Değer" çiftleri, attribute DisplayOrder
    /// (synchronizer ile aynı: DisplayOrder→Name) sırasıyla ", " join (ör. "Renk: Kırmızı, Beden: M"). Salt görüntü.</summary>
    private static string BuildAttributeSummary(
        Guid variantId,
        List<ProductAttribute> attributes,
        List<ProductAttributeValue> values,
        List<ProductVariantAttributeValue> links)
    {
        var valueById = values.ToDictionary(v => v.Id);
        var attributeById = attributes.ToDictionary(a => a.Id);
        var attributeOrder = attributes
            .Select((a, index) => (a.Id, Index: index))
            .ToDictionary(x => x.Id, x => x.Index);

        var parts = links
            .Where(l => l.ProductVariantId == variantId
                && valueById.ContainsKey(l.ProductAttributeValueId)
                && attributeById.ContainsKey(l.ProductAttributeId))
            .OrderBy(l => attributeOrder.GetValueOrDefault(l.ProductAttributeId, int.MaxValue))
            .Select(l => $"{attributeById[l.ProductAttributeId].Name}: {valueById[l.ProductAttributeValueId].Value}");

        return string.Join(", ", parts);
    }
}
