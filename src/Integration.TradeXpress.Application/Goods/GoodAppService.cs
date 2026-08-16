using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Application;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Vouchers;
using Integration.TradeXpress.Commodities;

namespace Integration.TradeXpress.Goods;

/// <summary>
/// Good (Mamül — perakende stok kartı) CRUD — company-scoped. Scalar alanlar HostCatalogCrudAppService tabanından;
/// GRAF (tedarikçiler drill'i + doküman/not + varyant sistemi) ve ana-tedarikçi auto-sync + türetilmiş satış fiyatı bu
/// serviste orkestre edilir. Görseller VARYANT seviyesinde MEDYA (<see cref="IEntityMediaAppService"/>) ile taşınır.
/// </summary>
[Authorize]
public class GoodAppService
    : CommodityCatalogAppService<Good, GoodGetDto, GoodListDto, GoodListRequestDto, GoodCreateDto, GoodUpdateDto>,
      IGoodAppService
{
    private const string GoodEntityName = "Good";
    private const string VariantImageEntityName = "GoodVariant";   // varyant-özel görsellerin agnostik EntityImage anahtarı

    private readonly IRepository<Good, Guid> _goodRepository;
    private readonly IRepository<GoodSupplier, Guid> _supplierRepository;
    private readonly IRepository<Account, Guid> _accountRepository;
    private readonly IRepository<SubAccount, Guid> _subAccountRepository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyRepository;
    private readonly IEntityVariantGraphService _entityVariant;
    private readonly IRepository<GoodVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IGoodPricingResolver _pricingResolver;
    private readonly IEntityDocumentAppService _documentService;
    private readonly IEntityNoteAppService _noteService;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly ICurrentCompany _currentCompany;
    private readonly Products.GoodToProductProjector _goodToProductProjector;

    public GoodAppService(
        IRepository<Good, Guid> repository,
        IRepository<GoodSupplier, Guid> supplierRepository,
        IRepository<Account, Guid> accountRepository,
        IRepository<SubAccount, Guid> subAccountRepository,
        IRepository<CurrencyUnit, Guid> currencyRepository,
        IEntityVariantGraphService entityVariant,
        IRepository<GoodVariantDetail, Guid> variantDetailRepository,
        IRepository<EntityVariant, Guid> variantRepository,
        IGoodPricingResolver pricingResolver,
        IEntityDocumentAppService documentService,
        IEntityNoteAppService noteService,
        IEntityMediaAppService entityMedia,
        ICurrentCompany currentCompany,
        Products.GoodToProductProjector goodToProductProjector)
        : base(repository)
    {
        _goodRepository = repository;
        _supplierRepository = supplierRepository;
        _accountRepository = accountRepository;
        _subAccountRepository = subAccountRepository;
        _currencyRepository = currencyRepository;
        _entityVariant = entityVariant;
        _variantDetailRepository = variantDetailRepository;
        _variantRepository = variantRepository;
        _pricingResolver = pricingResolver;
        _documentService = documentService;
        _noteService = noteService;
        _entityMedia = entityMedia;
        _currentCompany = currentCompany;
        _goodToProductProjector = goodToProductProjector;
        LocalizationResource = typeof(TradeXpressResource);

        CreatePolicyName = TradeXpressPermissions.Goods.Create;
        UpdatePolicyName = TradeXpressPermissions.Goods.Update;
        DeletePolicyName = TradeXpressPermissions.Goods.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Good entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Good; }
    }

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Good:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Good:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Good, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    /// <summary>Mamülün ürün aynası — iş <c>GoodToProductProjector</c>'da; burada yalnız yetki kapısı
    /// (ileri yöndeki <c>ProductAppService.ProjectToGoodAsync</c> ile birebir simetrik).</summary>
    public virtual async Task<Products.ProductGetDto> ProjectToProductAsync(Guid goodId)
    {
        return await _goodToProductProjector.ProjectAsync(goodId);
    }

    public virtual async Task<List<GoodListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        var scope = CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Good>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            // Fiyat zenginleştirmesi EnrichPickerListAsync hook'unda — base'in IMultiTenant-disable scope'u İÇİNDE
            // çalışır (host mamüllerin varyant fiyat satırları tenant filtresine takılmasın).
            return await GetPickerListCoreAsync(scope);
        }
    }

    // Mamül fiş satırı panelindeki varyant combo'su — bir mamülün AKTİF varyantları + varyant-başı fiyatı (GoodVariantDetail).
    // Ana varyant öncelikli, sonra koda göre. goodId zaten görünür bir mamül → varyantları EntityName+EntityId ile daraltılır.
    public virtual async Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid goodId)
    {
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var variants = await AsyncExecuter.ToListAsync(
                (await _variantRepository.GetQueryableAsync())
                    .Where(v => v.EntityName == GoodEntityName && v.EntityId == goodId && v.IsActive)
                    .Select(v => new { v.Id, v.Code, v.IsMain }));
            if (variants.Count == 0)
            {
                return new List<CommodityVariantOptionDto>();
            }

            var ids = variants.Select(v => v.Id).ToList();
            var details = (await AsyncExecuter.ToListAsync(
                    (await _variantDetailRepository.GetQueryableAsync()).Where(d => ids.Contains(d.EntityVariantId))))
                .ToDictionary(d => d.EntityVariantId);

            return variants
                .OrderByDescending(v => v.IsMain).ThenBy(v => v.Code)
                .Select(v =>
                {
                    details.TryGetValue(v.Id, out var d);
                    return new CommodityVariantOptionDto
                    {
                        Id = v.Id,
                        Code = v.Code,
                        IsMain = v.IsMain,
                        EntryPrice = d?.EntryPrice ?? 0m,
                        EntryPriceUnitId = d?.EntryPriceUnitId,
                        ExitPrice = d?.ExitPrice ?? 0m,
                        ExitPriceUnitId = d?.ExitPriceUnitId,
                    };
                })
                .ToList();
        }
    }

    /// <summary>Reçete paneli için mamül×varyant YASSI listesi — Metal <c>GetVariantLookupAsync</c> deseni. Global
    /// filtreler kapatılıp görünürlük ELLE yeniden kurulur (host mamülü tenant filtresine takılmasın; salt-okuma).
    /// Fiyat/birim SEÇİLİ varyantın <c>GoodVariantDetail</c>'inden — detayı olmayan varyant 0/null (fail-closed:
    /// uydurma fiyat yok). IsQuantity/PriceByQuantity ENTITY'den (varyant detayındaki IsQuantity hiç yazılmaz).</summary>
    public virtual async Task<List<CommodityVariantLookupDto>> GetVariantLookupAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var goodPredicate = CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Good>(CurrentTenant.Id, _currentCompany.Id);
            var variantPredicate = CompanyScopedQueryable.CompanyVisiblePredicate<EntityVariant>(CurrentTenant.Id, _currentCompany.Id);

            var rows = await AsyncExecuter.ToListAsync(
                from good in (await Repository.GetQueryableAsync()).Where(goodPredicate)
                join variant in (await _variantRepository.GetQueryableAsync()).Where(variantPredicate) on good.Id equals variant.EntityId
                where variant.EntityName == GoodEntityName && !variant.IsDeleted && !good.IsDeleted && variant.IsActive
                select new
                {
                    CommodityId = good.Id,
                    CommodityCode = good.Code,
                    CommodityName = good.Name,
                    VariantId = variant.Id,
                    VariantCode = variant.Code,
                    VariantName = variant.Name,
                    variant.IsMain,
                    good.IsQuantity,
                    good.PriceByQuantity,
                });

            var variantIds = rows.Select(r => r.VariantId).ToList();
            var details = (await AsyncExecuter.ToListAsync(
                    (await _variantDetailRepository.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId))))
                .ToDictionary(d => d.EntityVariantId);

            return rows
                .Select(r =>
                {
                    details.TryGetValue(r.VariantId, out var d);
                    return new CommodityVariantLookupDto
                    {
                        CommodityId = r.CommodityId,
                        CommodityCode = r.CommodityCode,
                        CommodityName = r.CommodityName,
                        VariantId = r.VariantId,
                        VariantCode = r.VariantCode,
                        VariantName = r.VariantName,
                        IsMain = r.IsMain,
                        IsQuantity = r.IsQuantity,
                        PriceByQuantity = r.PriceByQuantity,
                        EntryPrice = d?.EntryPrice ?? 0m,
                        EntryPriceUnitId = d?.EntryPriceUnitId,
                        ExitPrice = d?.ExitPrice ?? 0m,
                        ExitPriceUnitId = d?.ExitPriceUnitId,
                    };
                })
                .OrderBy(x => x.CommodityCode)
                .ThenByDescending(x => x.IsMain)
                .ThenBy(x => x.VariantCode)
                .ToList();
        }
    }

    // Liste zenginleştirmesi — kardeş emtia deseni (Metal/Jewelry/Stone): base GetListAsync'in IMultiTenant-disable
    // scope'u İÇİNDE çağrılır; host (TenantId=null) mamüllerin varyant/medya/fiyat satırları tenant filtresine takılmaz.
    // (Eski GetListAsync override'ı zenginleştirmeyi scope KAPANDIKTAN sonra yapıyordu → host mamüllerde thumbnail/fiyat boştu.)
    protected override async Task EnrichListAsync(List<Good> entities, List<GoodListDto> dtos)
    {
        await base.EnrichListAsync(entities, dtos);
        await EnrichPricingAsync(dtos);      // fiyat artık Good'da DEĞİL → ana varyantta; voucher-liste bunu okur
        await EnrichPreviewsAsync(dtos);
    }

    // Picker (combo) görsel çizmez → önizleme batch'i ATLANIR (Metal deseni; base EnrichListAsync'e düşmesin).
    // Fiyat KALIR — voucher paneli GoodListDto.EntryPrice/ExitPrice okur.
    protected override Task EnrichPickerListAsync(List<Good> entities, List<GoodListDto> dtos)
    {
        return EnrichPricingAsync(dtos);
    }

    // GoodListDto.ImagePreviewUrl'i ana varyantın varsayılan medyasının poster'ından doldurur (tek batch; N+1 yok).
    private async Task EnrichPreviewsAsync(IReadOnlyList<GoodListDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var mainVariants = await _entityVariant.GetMainVariantMapAsync(GoodEntityName, dtos.Select(d => d.Id).ToList());
        if (mainVariants.Count == 0)
        {
            return;
        }

        var posters = await _entityMedia.GetDefaultPosterMapAsync(VariantImageEntityName, mainVariants.Values.ToList());
        foreach (var dto in dtos)
        {
            if (mainVariants.TryGetValue(dto.Id, out var vId) && posters.TryGetValue(vId, out var url))
            {
                dto.ImagePreviewUrl = url;
            }
        }
    }

    // GoodListDto fiyatını (alış/satış + birim) ana varyant GoodVariantDetail'inden doldurur (IGoodPricingResolver; DRY).
    private async Task EnrichPricingAsync(IReadOnlyList<GoodListDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var pricing = await _pricingResolver.ResolveAsync(items.Select(i => i.Id).ToList());
        foreach (var i in items)
        {
            if (pricing.TryGetValue(i.Id, out var p))
            {
                i.EntryPrice = p.EntryPrice;
                i.EntryPriceUnitId = p.EntryPriceUnitId;
                i.ExitPrice = p.ExitPrice;
                i.ExitPriceUnitId = p.ExitPriceUnitId;
            }
        }
    }

    protected override Expression<Func<Good, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Good>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<Good> ApplyFallbackSort(IQueryable<Good> query, GoodListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    // ── Scalar map (graf HARİÇ — o Create/Update override'larında) ──

    protected override Task<Good> MapToEntityAsync(GoodCreateDto createInput)
    {
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard).
        var entity = new Good(createInput.Code, createInput.Name, CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany));
        ApplyScalars(entity, createInput.Brand, createInput.Model, createInput.Kind, createInput.Type,
            createInput.Color, createInput.Size, createInput.Category, createInput.GroupCode, createInput.StockUnitCode,
            createInput.VatPurchaseRate, createInput.VatSaleRate, createInput.OtvRate, createInput.WithholdingRate,
            createInput.IsQuantity, createInput.PriceByQuantity, createInput.PriceTypeChange, createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Good entity)
    {
        return EnsureCodeUniqueAsync(
            entity, x => x.CompanyId == entity.CompanyId && x.Code == entity.Code,
            "TradeXpress:Good:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(GoodUpdateDto updateInput, Good entity)
    {
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Good.Code), EntityFieldConsts.CodeMinLength, GoodConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.Code == code,
            "TradeXpress:Good:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        ApplyScalars(entity, updateInput.Brand, updateInput.Model, updateInput.Kind, updateInput.Type,
            updateInput.Color, updateInput.Size, updateInput.Category, updateInput.GroupCode, updateInput.StockUnitCode,
            updateInput.VatPurchaseRate, updateInput.VatSaleRate, updateInput.OtvRate, updateInput.WithholdingRate,
            updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange, updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    private static void ApplyScalars(
        Good e, string? brand, string? model, string? kind, string? type, string? color, string? size,
        string? category, string? groupCode, string? stockUnitCode,
        decimal vatPurchase, decimal vatSale, decimal otv, decimal withholding,
        bool isQuantity, bool priceByQuantity, bool priceTypeChange, string? description)
    {
        // Fiyat DEĞERLERİ + Min/Max artık varyantta (GoodVariantDetail) — burada yalnız sınıflandırma/birim/vergi/fiyat-tipi.
        e.SetClassification(brand, model, kind, type, color, size, category, groupCode);
        e.SetStockUnit(stockUnitCode);
        e.SetTaxes(vatPurchase, vatSale, otv, withholding);
        e.SetPricingType(isQuantity, priceByQuantity, priceTypeChange);
        e.SetDescription(description);
    }

    // ── Graf: Create/Update override → scalar save (base) + tedarikçiler + görseller + ana-tedarikçi auto-sync ──

    public override async Task<GoodGetDto> CreateAsync(GoodCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Suppliers, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(dto.Id);
    }

    public override async Task<GoodGetDto> UpdateAsync(Guid id, GoodUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Suppliers, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(id);
    }

    /// <summary>Grafı saklar: tedarikçiler drill'i (replace-all) + doküman/not (agnostik ReplaceFor) + VARYANT sistemi
    /// (nitelik/değer graf-diff → synchronizer kartezyen üretimi → varyant özelleştirmeleri; görsel VARYANT medyasında).
    /// Tedarikçi bilgisi YALNIZ drill seviyesinde (her tedarikçi kendi fiyat/birim/vergi/gününü taşır).</summary>
    private async Task SaveGraphAsync(
        Guid goodId,
        List<GoodSupplierDto> suppliers,
        List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes,
        List<EntityAttributeGraphDto> attributes,
        List<GoodVariantGraphDto> variants,
        List<EntityMediaLinkEditDto> media)
    {
        var good = await _goodRepository.GetAsync(goodId);

        // Cari hesap (Account) ZORUNLU — alt hesap opsiyonel (cari hesap tek başına yeterli).
        var finalSuppliers = suppliers
            .Where(s => s.AccountId != Guid.Empty)
            .Select(s => new SupplierTerms(s.AccountId, s.SubAccountId, s.Price, s.CurrencyUnitId, s.TaxIncluded, s.LeadDays))
            .ToList();

        // Replace-all: mevcut satırları sil, yeniden yaz.
        var existing = await AsyncExecuter.ToListAsync(
            (await _supplierRepository.GetQueryableAsync()).Where(x => x.GoodId == goodId));
        await _supplierRepository.DeleteManyAsync(existing, autoSave: false);
        foreach (var t in finalSuppliers)
        {
            var row = new GoodSupplier(good.CompanyId, goodId, t.AccountId, t.SubAccountId);
            row.SetTerms(t.Price, t.CurrencyUnitId, t.TaxIncluded, t.LeadDays);
            await _supplierRepository.InsertAsync(row, autoSave: false);
        }

        // Dokümanlar + notlar + KAYIT-GENELİ MEDYA — entity-agnostik ("Good" bağlamı).
        //
        // Medya 2026-08-06'da eklendi (Hakan kuralı: her medya tipi İKİ bağlamı da taşır). Önceden yalnız
        // VARYANT medyası vardı ve doc "görsel ana kayıtta DEĞİL" diyordu — oysa Product'ta kayıt-geneli
        // medya varken Good'da hiç olmaması, "ürünün aynası" olması gereken mamül formunu görselsiz
        // bırakıyordu. İki depo AYRIDIR ve biri diğerinden TÜRETİLMEZ; push zinciri varyant→kayıt
        // fallback'iyle okur (MarketplacePushImageResolver).
        await _documentService.ReplaceForAsync(GoodEntityName, goodId, documents);
        await _noteService.ReplaceForAsync(GoodEntityName, goodId, notes);
        await _entityMedia.ReplaceForAsync(GoodEntityName, goodId, good.CompanyId, media);

        // Varyant sistemi — JENERİK agnostik servise delege ("Good" bağlamı). Çekirdek (nitelik/değer/varyant) serviste;
        // Good-ÖZEL fiyat/stok uzantısı saveExtensionAsync callback'iyle GoodVariantDetail'e (çözülen DB varyanta) bağlanır.
        await _entityVariant.SaveGraphAsync(
            GoodEntityName, goodId, good.CompanyId, good.Name, attributes, variants,
            saveExtensionAsync: (dto, variantId) => SaveVariantDetailAsync(good.CompanyId, dto, variantId),
            ownerCode: good.Code);   // niteliksiz tek varyant sahibin kodunu izler ("ANAVARYANT" değil)
    }

    // Varyant fiyat/stok uzantısı (GoodVariantDetail) — çözülen DB varyanta bağlar (yoksa ekle/varsa güncelle).
    // Stok BİRİMİ/adet-bazlı ana mamülde (Good) kalır — burada set edilmez.
    private async Task SaveVariantDetailAsync(Guid? companyId, EntityVariantGraphDto dto, Guid variantId)
    {
        if (dto is not GoodVariantGraphDto g)
        {
            return;
        }

        var detail = await _variantDetailRepository.FirstOrDefaultAsync(x => x.EntityVariantId == variantId)
            ?? new GoodVariantDetail(companyId, variantId);
        detail.SetQuantityLimits(g.MinQuantity, g.MaxQuantity);
        detail.SetPurchasePrice(g.EntryPrice, g.EntryPriceUnitId, g.EntryPriceTaxIncluded);
        detail.SetMargin(new MarginSetting(g.MarginType, g.MarginValue), g.ExitPriceTaxIncluded);
        // Sabit Fiyat (FinalPrice) → satış birimi bağımsız (mutlak fiyat); diğer marjlarda RecomputeExitPrice alış birimini zorlar.
        if (g.MarginType == MarginType.FinalPrice)
        {
            detail.SetSalePriceUnit(g.ExitPriceUnitId);
        }

        if (detail.Id == Guid.Empty)
        {
            await _variantDetailRepository.InsertAsync(detail, autoSave: true);
        }
        else
        {
            await _variantDetailRepository.UpdateAsync(detail, autoSave: true);
        }

        // Varyant-özel ekler — agnostik ("GoodVariant" bağlamı): MEDYA (görsel+video link) + doküman + not.
        await _entityMedia.ReplaceForAsync(VariantImageEntityName, variantId, companyId, g.Media);
        await _documentService.ReplaceForAsync(VariantImageEntityName, variantId, g.Documents);
        await _noteService.ReplaceForAsync(VariantImageEntityName, variantId, g.Notes);
    }

    // ── Get: scalar (base) + tedarikçiler (enrich) + görseller + varyant grafı (fiyat varyantta). ──

    public override async Task<GoodGetDto> GetAsync(Guid id)
    {
        var dto = await base.GetAsync(id);

        dto.Suppliers = await LoadSuppliersAsync(id);

        // Dokümanlar + notlar — entity-agnostik ("Good" bağlamı). Id taşınır → doküman indirmesi (DownloadAsync) için.
        dto.Documents = (await _documentService.GetForAsync(GoodEntityName, id))
            .Select(d => new EntityDocumentEditDto
            {
                Id = d.Id,
                FileName = d.FileName,
                BlobName = d.BlobName,
                ContentType = d.ContentType,
                Size = d.Size,
                Description = d.Description,
                DisplayOrder = d.DisplayOrder,
            })
            .ToList();
        dto.Notes = (await _noteService.GetForAsync(GoodEntityName, id))
            .Select(n => new EntityNoteEditDto
            {
                Id = n.Id,
                Title = n.Title,
                Text = n.Text,
                DisplayOrder = n.DisplayOrder,
                CreationTime = n.CreationTime,
            })
            .ToList();

        // Varyant grafı — JENERİK agnostik servisten (çekirdek) + Good-özel fiyat/stok uzantısı (GoodVariantDetail).
        var graph = await _entityVariant.LoadGraphAsync(GoodEntityName, id);
        dto.Attributes = graph.Attributes;
        dto.Variants = await ProjectVariantsAsync(graph.Variants);

        return dto;
    }

    private async Task<List<GoodSupplierDto>> LoadSuppliersAsync(Guid goodId)
    {
        var rows = await AsyncExecuter.ToListAsync(
            (await _supplierRepository.GetQueryableAsync())
                .Where(x => x.GoodId == goodId)
                .OrderBy(x => x.CreationTime).ThenBy(x => x.Id));
        if (rows.Count == 0)
        {
            return new List<GoodSupplierDto>();
        }

        var accountCodes = await CodeMapAsync(_accountRepository, rows.Select(r => r.AccountId));
        var subAccountCodes = await CodeMapAsync(_subAccountRepository, rows.Where(r => r.SubAccountId != null).Select(r => r.SubAccountId!.Value));
        var currencyCodes = await CodeMapAsync(_currencyRepository, rows.Where(r => r.CurrencyUnitId != null).Select(r => r.CurrencyUnitId!.Value));

        return rows.Select(r => new GoodSupplierDto
        {
            Id = r.Id,
            AccountId = r.AccountId,
            SubAccountId = r.SubAccountId,
            Price = r.Price,
            CurrencyUnitId = r.CurrencyUnitId,
            TaxIncluded = r.TaxIncluded,
            LeadDays = r.LeadDays,
            AccountCode = accountCodes.GetValueOrDefault(r.AccountId),
            SubAccountCode = r.SubAccountId is { } sa ? subAccountCodes.GetValueOrDefault(sa) : null,
            CurrencyCode = r.CurrencyUnitId is { } cu ? currencyCodes.GetValueOrDefault(cu) : null,
        }).ToList();
    }

    // Id → Code sözlüğü (batch; N+1 yok). Kod taşıyan entity'ler (Account/SubAccount/CurrencyUnit) için.
    private async Task<Dictionary<Guid, string>> CodeMapAsync<TEntity>(IRepository<TEntity, Guid> repo, IEnumerable<Guid> ids)
        where TEntity : class, IEntity<Guid>
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var rows = await AsyncExecuter.ToListAsync(
            (await repo.GetQueryableAsync()).Where(x => idList.Contains(x.Id)));
        return rows
            .Select(x => new { x.Id, Code = (string?)x.GetType().GetProperty("Code")?.GetValue(x) })
            .Where(x => x.Code != null)
            .ToDictionary(x => x.Id, x => x.Code!);
    }

    // ── Varyant sistemi — JENERİK agnostik servise delege (tüm mantık EntityVariantGraphService'te; DRY) ──

    public virtual Task<List<GoodVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input)
    {
        // Çekirdek üretim jenerik serviste; Good türevine re-project (fiyat/stok default — kullanıcı sonra düzenler).
        return Task.FromResult(_entityVariant.GenerateVariants(input).Select(CopyCore).ToList());
    }

    // Mamül silinmeden ÖNCE (guard'lar geçti) — varyant grafı (+ Good detay uzantısı + varyant medyası) + tedarikçiler + doküman/not temizlenir.
    protected override async Task BeforeDeleteAsync(Good entity)
    {
        await _entityVariant.DeleteForAsync(
            GoodEntityName, entity.Id,
            deleteExtensionAsync: async ids =>
            {
                await _variantDetailRepository.DeleteAsync(d => ids.Contains(d.EntityVariantId), autoSave: true);
                foreach (var vid in ids)
                {
                    await _entityMedia.ReplaceForAsync(VariantImageEntityName, vid, null, new List<EntityMediaLinkEditDto>());
                    await _documentService.ReplaceForAsync(VariantImageEntityName, vid, new List<EntityDocumentEditDto>());
                    await _noteService.ReplaceForAsync(VariantImageEntityName, vid, new List<EntityNoteEditDto>());
                }
            });

        var suppliers = await AsyncExecuter.ToListAsync(
            (await _supplierRepository.GetQueryableAsync()).Where(x => x.GoodId == entity.Id));
        await _supplierRepository.DeleteManyAsync(suppliers, autoSave: true);

        await _documentService.ReplaceForAsync(GoodEntityName, entity.Id, new List<EntityDocumentEditDto>());
        await _noteService.ReplaceForAsync(GoodEntityName, entity.Id, new List<EntityNoteEditDto>());
    }

    // Jenerik çekirdek varyantları (base) Good türevine + fiyat/stok uzantısıyla (GoodVariantDetail) zenginleştirir.
    private async Task<List<GoodVariantGraphDto>> ProjectVariantsAsync(List<EntityVariantGraphDto> baseVariants)
    {
        if (baseVariants.Count == 0)
        {
            return new List<GoodVariantGraphDto>();
        }

        var variantIds = baseVariants.Select(v => v.Id).ToList();
        var details = (await AsyncExecuter.ToListAsync(
                (await _variantDetailRepository.GetQueryableAsync()).Where(d => variantIds.Contains(d.EntityVariantId))))
            .ToDictionary(d => d.EntityVariantId);

        var result = new List<GoodVariantGraphDto>();
        foreach (var v in baseVariants)
        {
            var g = CopyCore(v);
            if (details.TryGetValue(v.Id, out var d))
            {
                g.MinQuantity = d.MinQuantity;
                g.MaxQuantity = d.MaxQuantity;
                g.EntryPrice = d.EntryPrice;
                g.EntryPriceUnitId = d.EntryPriceUnitId;
                g.EntryPriceTaxIncluded = d.EntryPriceTaxIncluded;
                var m = d.Margin ?? MarginSetting.Passthrough;
                g.MarginType = m.Type;
                g.MarginValue = m.Value;
                g.ExitPrice = d.ExitPrice;
                g.ExitPriceUnitId = d.ExitPriceUnitId;
                g.ExitPriceTaxIncluded = d.ExitPriceTaxIncluded;
            }

            // Varyant-özel MEDYA link'leri — merkezi kütüphaneye referans (görsel+video birlikte).
            g.Media = await _entityMedia.GetForAsync(VariantImageEntityName, v.Id);

            // Varyant-özel doküman + not — agnostik ("GoodVariant" bağlamı).
            g.Documents = (await _documentService.GetForAsync(VariantImageEntityName, v.Id))
                .Select(dd => new EntityDocumentEditDto
                {
                    Id = dd.Id,
                    FileName = dd.FileName,
                    BlobName = dd.BlobName,
                    ContentType = dd.ContentType,
                    Size = dd.Size,
                    Description = dd.Description,
                    DisplayOrder = dd.DisplayOrder,
                })
                .ToList();
            g.Notes = (await _noteService.GetForAsync(VariantImageEntityName, v.Id))
                .Select(nn => new EntityNoteEditDto
                {
                    Id = nn.Id,
                    Title = nn.Title,
                    Text = nn.Text,
                    DisplayOrder = nn.DisplayOrder,
                    CreationTime = nn.CreationTime,
                })
                .ToList();

            result.Add(g);
        }

        return result;
    }

    // Çekirdek alanları (jenerik EntityVariantGraphDto) Good türevine kopyalar (fiyat/stok default).
    private static GoodVariantGraphDto CopyCore(EntityVariantGraphDto v)
    {
        return new GoodVariantGraphDto
        {
            Id = v.Id,
            ClientKey = v.ClientKey,
            IsDeleted = v.IsDeleted,
            IsMain = v.IsMain,
            Code = v.Code,
            Name = v.Name,
            Description = v.Description,
            IsActive = v.IsActive,
            Barcode = v.Barcode,
            Gtin = v.Gtin,
            Mpn = v.Mpn,
            Oem = v.Oem,
            StockQuantity = v.StockQuantity,
            AttributeSummary = v.AttributeSummary,
            CombinationKey = v.CombinationKey,
        };
    }

    private readonly record struct SupplierTerms(
        Guid AccountId, Guid? SubAccountId, decimal Price, Guid? CurrencyUnitId, bool TaxIncluded, int LeadDays);
}
