using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.MultiCompany;

namespace Integration.TradeXpress.Metals;

/// <summary>
/// Metal (Maden) CRUD. FollowingUnit ZORUNLU; Factor &gt;0 (üst sınır yok). Görünürlük/guard/liste/picker davranışı
/// <see cref="FollowingUnitCatalogAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından
/// (host kataloğu + tenant kendi kayıtları; picker birim düzeni → Factor desc → Code asc).
/// </summary>
[Authorize]
public class MetalAppService
    : FollowingUnitCatalogAppService<Metal, MetalGetDto, MetalListDto, MetalListRequestDto, MetalCreateDto, MetalUpdateDto>,
      IMetalAppService
{
    private const string MetalEntityName = "Metal";
    private const string VariantImageEntityName = "MetalVariant";   // varyant-özel medya/doküman/notun agnostik bağlam anahtarı

    private readonly CommodityAgnosticGraph _graph;
    private readonly IRepository<MetalVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<Integration.TradeXpress.Variants.EntityVariant, Guid> _entityVariantRepository;
    private readonly ICurrentCompany _currentCompany;

    public MetalAppService(
        IRepository<Metal, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        CommodityAgnosticGraph graph,
        IRepository<MetalVariantDetail, Guid> variantDetailRepository,
        IRepository<Integration.TradeXpress.Variants.EntityVariant, Guid> entityVariantRepository,
        ICurrentCompany currentCompany)
        : base(repository, unitRepository)
    {
        _graph = graph;
        _variantDetailRepository = variantDetailRepository;
        _entityVariantRepository = entityVariantRepository;
        _currentCompany = currentCompany;
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): combo ✎/+ görünürlüğüyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Metals.Create;
        UpdatePolicyName = TradeXpressPermissions.Metals.Update;
        DeletePolicyName = TradeXpressPermissions.Metals.Delete;
    }

    public override async Task<PagedResultDto<MetalListDto>> GetListAsync(MetalListRequestDto input)
    {
        var page = await base.GetListAsync(input);
        
        if (page.Items.Count > 0)
        {
            var mainVariants = await _graph.GetMainVariantMapAsync(MetalEntityName, page.Items.Select(d => d.Id).ToList());
            if (mainVariants.Count > 0)
            {
                var vIds = mainVariants.Values.Distinct().ToList();
                var details = (await AsyncExecuter.ToListAsync(
                        (await _variantDetailRepository.GetQueryableAsync()).Where(d => vIds.Contains(d.EntityVariantId))))
                    .ToDictionary(d => d.EntityVariantId);

                foreach (var dto in page.Items)
                {
                    if (mainVariants.TryGetValue(dto.Id, out var vId) && details.TryGetValue(vId, out var d))
                    {
                        dto.LaborType = d.LaborType;
                        dto.EntryLabor = d.EntryLabor;
                        dto.EntryLaborUnitId = d.EntryLaborUnitId;
                        dto.ExitLabor = d.ExitLabor;
                        dto.ExitLaborUnitId = d.ExitLaborUnitId;

                        // "Değiştirilebilir" bayrakları (2026-08-07 G2): DTO'da vardı ama HİÇ doldurulmuyordu →
                        // cari işlem paneli işçiliği her madende salt-okunur gösteriyordu (ACIK-ISLER:53 #4).
                        dto.LaborTypeChange = d.LaborTypeChange;
                        dto.EntryLaborChange = d.EntryLaborChange;
                        dto.ExitLaborChange = d.ExitLaborChange;
                    }
                    else
                    {
                        dto.LaborType = Integration.TradeXpress.Vouchers.MetalLaborType.Amount;
                    }
                }
            }
        }

        return page;
    }

    protected override async Task EnrichPickerListAsync(List<Metal> entities, List<MetalListDto> dtos)
    {
        await base.EnrichPickerListAsync(entities, dtos);
        
        if (dtos.Count > 0)
        {
            var mainVariants = await _graph.GetMainVariantMapAsync(MetalEntityName, dtos.Select(d => d.Id).ToList());
            if (mainVariants.Count > 0)
            {
                var vIds = mainVariants.Values.Distinct().ToList();
                // IMultiTenant DE kapatılır — GetVariantLookupAsync ile aynı gerekçe (host işçilik satırı elenip
                // EntryLabor sessizce 0'a düşmesin); salt-okuma zenginleştirme.
                using (DataFilter.Disable<IMultiTenant>())
                using (DataFilter.Disable<ICompanyScoped>())
                {
                    var details = (await AsyncExecuter.ToListAsync(
                            (await _variantDetailRepository.GetQueryableAsync()).Where(d => vIds.Contains(d.EntityVariantId))))
                        .ToDictionary(d => d.EntityVariantId);

                    foreach (var dto in dtos)
                    {
                        if (mainVariants.TryGetValue(dto.Id, out var vId) && details.TryGetValue(vId, out var d))
                        {
                            dto.LaborType = d.LaborType;
                            dto.EntryLabor = d.EntryLabor;
                            dto.EntryLaborUnitId = d.EntryLaborUnitId;
                            dto.ExitLabor = d.ExitLabor;
                            dto.ExitLaborUnitId = d.ExitLaborUnitId;
                            dto.LaborTypeChange = d.LaborTypeChange;      // G2 — picker yolu da bayrakları taşır
                            dto.EntryLaborChange = d.EntryLaborChange;
                            dto.ExitLaborChange = d.ExitLaborChange;
                        }
                        else
                        {
                            dto.LaborType = Integration.TradeXpress.Vouchers.MetalLaborType.Amount;
                        }
                    }
                }
            }
        }
    }
    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Metal entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Metal; }
    }

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Metal:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Metal:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Metal, string>> PickerOrderSelector
    {
        get { return x => x.Code; }   // kullanılmaz — picker composite sırayı tabandaki override kurar
    }

    protected override Guid FollowingUnitIdOf(Metal entity)
    {
        return entity.FollowingUnitId;
    }

    protected override decimal CompositeFactorOf(Metal entity)
    {
        return entity.Factor;
    }

    protected override string CodeOf(Metal entity)
    {
        return entity.Code;
    }

    protected override Task<Metal> MapToEntityAsync(MetalCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard):
        // createInput.CompanyId'ye güvenmek sahipsiz (holding) ya da YABANCI şirkete ait kayıt üretilmesine
        // izin veriyordu; holding kaydı tenant'ın tüm şirketlerine görünür olduğundan cross-company etkiydi.
        var entity = new Metal(
            createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value,
            CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany),
            createInput.Factor, createInput.FactorChange,
            createInput.IsQuantity, createInput.StableQuantity);
        entity.SetBarcode(createInput.Barcode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Metal entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code && x.CompanyId == entity.CompanyId, "TradeXpress:Metal:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(MetalUpdateDto updateInput, Metal entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index (TenantId, Code) ile hizalı.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Metal.Code), EntityFieldConsts.CodeMinLength, MetalConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.Code == code && x.CompanyId == entity.CompanyId,
            "TradeXpress:Metal:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFactor(updateInput.Factor);
        entity.SetFactorChange(updateInput.FactorChange);
        entity.SetQuantityTracking(updateInput.IsQuantity, updateInput.StableQuantity);
        entity.SetBarcode(updateInput.Barcode);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    protected override async Task BeforeDeleteAsync(Metal entity)
    {
        // Agnostik graf (varyant + varyant medyası + doküman/not) temizliği — yetim önleme.
        await _graph.DeleteAsync(MetalEntityName, VariantImageEntityName, entity.Id);
    }

    protected override async Task EnrichGetAsync(Metal entity, MetalGetDto dto)
    {
        await base.EnrichGetAsync(entity, dto);

        var graph = await _graph.LoadAsync(MetalEntityName, VariantImageEntityName, entity.Id);
        dto.Media = graph.Media;
        dto.Documents = graph.Documents;
        dto.Notes = graph.Notes;
        dto.Attributes = graph.Attributes;

        // Map agnostic variants to Metal-specific variants
        var metalVariants = ObjectMapper.Map<List<EntityVariantGraphDto>, List<MetalVariantGraphDto>>(graph.Variants);

        if (metalVariants.Any())
        {
            var variantIds = metalVariants.Select(x => x.Id).ToList();
            var details = await AsyncExecuter.ToListAsync(
                (await _variantDetailRepository.GetQueryableAsync()).Where(x => variantIds.Contains(x.EntityVariantId))
            );
            var detailDict = details.ToDictionary(x => x.EntityVariantId);

            foreach (var v in metalVariants)
            {
                if (detailDict.TryGetValue(v.Id, out var d))
                {
                    v.LaborType = d.LaborType;
                    v.EntryLabor = d.EntryLabor;
                    v.EntryLaborUnitId = d.EntryLaborUnitId;
                    v.ExitLabor = d.ExitLabor;
                    v.ExitLaborUnitId = d.ExitLaborUnitId;

                    // G2: graf okuma yolu bayrakları + varyant CostUnitId'sini taşımalı — aksi halde form
                    // bunları false/null okur, Save aynısını geri yazar ve veri yine kaybolur (round-trip kırılır).
                    v.LaborTypeChange = d.LaborTypeChange;
                    v.EntryLaborChange = d.EntryLaborChange;
                    v.ExitLaborChange = d.ExitLaborChange;
                    v.CostUnitId = d.CostUnitId;
                }
                else
                {
                    v.LaborType = MetalLaborType.Amount;
                }
            }
        }

        dto.Variants = metalVariants;
    }

    protected override async Task EnrichListAsync(List<Metal> entities, List<MetalListDto> dtos)
    {
        await base.EnrichListAsync(entities, dtos);

        // Grid önizlemesi ana varyantın varsayılan poster'ından gelir — görseller VARYANT seviyesindedir
        // (Stone/Jewelry/Good deseni; maden-düzeyi tekil görsel 2026-07-31'de emekli edildi). Tek batch (N+1 yok).
        var variantPreviews = await _graph.GetVariantPreviewMapAsync(
            MetalEntityName, VariantImageEntityName, dtos.Select(d => d.Id).ToList());
        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].ImagePreviewUrl = variantPreviews.GetValueOrDefault(dtos[i].Id);
        }
    }

    // ── Agnostik graf: Create/Update override → scalar save (base) + graf (doküman/not + nitelik/varyant). Fiyat/stok uzantısı YOK (maden fiyatı milyem/işçilik). ──

    public override async Task<MetalGetDto> CreateAsync(MetalCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Name, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(dto.Id);
    }

    public override async Task<MetalGetDto> UpdateAsync(Guid id, MetalUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Name, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(id);
    }

    // Uydu kayıtların (varyant/nitelik/medya/detay) şirketi SAHİP MADENDEN türer — Stone/Jewelry/Good/Product ile
    // aynı desen. ÖNCE `companyId: null` geçiliyordu ("maden company-scoped değil" inancıyla); görev #4 madeni
    // ICompanyOwned yaptıktan sonra bu inanç GEÇERSİZ kaldı ve null geçmek DbContext'in auto-stamp'ını devreye
    // sokup varyanta WORKING company'yi basıyordu. Sonuç: fan-out ile başka şirkete kopyalanan madenin varyantları
    // (ana varyant dahil) o şirkette görünmez oluyordu — canlıda 4 satırda gerçekleşti.
    private async Task SaveGraphAsync(
        Guid metalId, string ownerName, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<MetalVariantGraphDto> variants,
        List<EntityMediaLinkEditDto> media)
    {
        var metal = await Repository.GetAsync(metalId);

        await _graph.SaveAsync(
            MetalEntityName, VariantImageEntityName, metalId, metal.CompanyId, ownerName,
            documents, notes, attributes, variants,
            additionalSaveAction: (dto, variantId) =>
                SaveVariantDetailAsync(metal.CompanyId, (MetalVariantGraphDto)dto, variantId),
            media: media,
            ownerCode: metal.Code);   // niteliksiz tek varyant sahibin kodunu izler ("ANAVARYANT" değil)
    }

    private async Task SaveVariantDetailAsync(Guid? companyId, MetalVariantGraphDto dto, Guid variantId)
    {
        var detail = await _variantDetailRepository.FirstOrDefaultAsync(x => x.EntityVariantId == variantId);
        var isNew = detail == null;

        if (isNew)
        {
            detail = new MetalVariantDetail(companyId, variantId);
        }

        // 2026-08-07 G2: bayraklar ve varyant CostUnitId'si SABİT false/null yazılıyordu — kullanıcının ilk
        // Save'i seed madenlerin (86/86) işçilik bayraklarını geri dönüşsüz siliyordu. Artık DTO'dan gelir.
        detail.SetLabor(
            dto.LaborType, dto.LaborTypeChange,
            dto.EntryLabor, dto.EntryLaborUnitId, dto.EntryLaborChange,
            dto.ExitLabor, dto.ExitLaborUnitId, dto.ExitLaborChange,
            dto.CostUnitId);

        if (isNew)
        {
            await _variantDetailRepository.InsertAsync(detail, autoSave: false);
        }
        else
        {
            await _variantDetailRepository.UpdateAsync(detail, autoSave: false);
        }
    }

    public virtual Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input)
    {
        return Task.FromResult(_graph.GenerateVariants(input));
    }

    public virtual async Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid metalId)
    {
        var variants = await _graph.GetVariantPickerAsync(MetalEntityName, metalId);
        if (variants.Count == 0)
        {
            return variants;
        }

        var ids = variants.Select(v => v.Id).ToList();
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var details = (await AsyncExecuter.ToListAsync(
                    (await _variantDetailRepository.GetQueryableAsync()).Where(d => ids.Contains(d.EntityVariantId))))
                .ToDictionary(d => d.EntityVariantId);

            foreach (var v in variants)
            {
                if (details.TryGetValue(v.Id, out var d))
                {
                    v.LaborType = d.LaborType;
                    v.EntryLabor = d.EntryLabor;
                    v.EntryLaborUnitId = d.EntryLaborUnitId;
                    v.ExitLabor = d.ExitLabor;
                    v.ExitLaborUnitId = d.ExitLaborUnitId;

                    // A4 (ACIK-ISLER:51): panel işçilik kilidini SEÇİLİ varyantın bayrağından okumalı — bunlar
                    // taşınmadığı için hangi varyant seçilirse seçilsin ANA varyantın işçiliği tahsil ediliyordu.
                    v.LaborTypeChange = d.LaborTypeChange;
                    v.EntryLaborChange = d.EntryLaborChange;
                    v.ExitLaborChange = d.ExitLaborChange;
                }
                else
                {
                    v.LaborType = Integration.TradeXpress.Vouchers.MetalLaborType.Amount;
                    v.EntryLabor = 0m;
                    v.ExitLabor = 0m;
                }
            }
        }

        return variants;
    }

    protected override Expression<Func<Metal, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Metal>(CurrentTenant.Id, _currentCompany.Id);
    }

    public virtual async Task<List<MetalVariantLookupDto>> GetVariantLookupAsync()
    {
        List<(Guid CommodityId, string MetalCode, string MetalName, Guid? VariantId, string VariantCode, string VariantName, bool IsMain, bool IsQuantity, decimal StableQuantity)> rows;
        using (DataFilter.Disable<Volo.Abp.MultiTenancy.IMultiTenant>())
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var metalsQuery = await Repository.GetQueryableAsync();
            var variantsQuery = await _entityVariantRepository.GetQueryableAsync();

            var metalPredicate = CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Metal>(CurrentTenant.Id, _currentCompany.Id);
            var variantPredicate = CompanyScopedQueryable.CompanyVisiblePredicate<Integration.TradeXpress.Variants.EntityVariant>(CurrentTenant.Id, _currentCompany.Id);

            var baseQuery = from metal in metalsQuery.Where(metalPredicate)
                            join variant in variantsQuery.Where(variantPredicate) on metal.Id equals variant.EntityId
                            where variant.EntityName == MetalEntityName && !variant.IsDeleted && !metal.IsDeleted
                            select new
                            {
                                CommodityId    = metal.Id,
                                MetalCode      = metal.Code,
                                MetalName      = metal.Name,
                                VariantId      = variant.Id,
                                VariantCode    = variant.Code,
                                VariantName    = variant.Name,
                                IsMain         = variant.IsMain,
                                IsQuantity     = metal.IsQuantity,
                                StableQuantity = metal.StableQuantity
                            };

            var raw = await AsyncExecuter.ToListAsync(baseQuery);
            rows = raw.Select(r => (r.CommodityId, r.MetalCode, r.MetalName, (Guid?)r.VariantId, r.VariantCode, r.VariantName, r.IsMain, r.IsQuantity, r.StableQuantity)).ToList();
            Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Logger, "GetVariantLookupAsync returned {Count} rows for Tenant: {Tenant}, Company: {Company}", rows.Count, CurrentTenant.Id, _currentCompany.Id);
        }

        Dictionary<Guid, MetalVariantDetail> details;
        // IMultiTenant DE kapatılır (kod-inceleme bulgusu): MetalVariantDetail IMultiTenant olduğundan, host'a ait
        // işçilik satırları tenant working-context'inde ELENİR ve EntryLabor sessizce 0 dönerdi. PayFactor 0 olduğunda
        // maliyet motoru işçilik birimini hiç aramaz → MissingRate bile üretilmez, yani çözücünün fiyatlayıp sıraladığı
        // işçilik bacağı uygulanan reçetede iz bırakmadan kaybolurdu. Sunucu tarafında aynı tuzak zaten kapatılmıştı
        // (SubstitutionCalculationAppService: "host satırları elenirdi → işçilik sessizce 0'a düşer"); okuma-only.
        using (DataFilter.Disable<IMultiTenant>())
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var variantIds = rows.Select(r => r.VariantId.GetValueOrDefault()).ToList();
            var detailsQuery = (await _variantDetailRepository.GetQueryableAsync())
                .Where(d => variantIds.Contains(d.EntityVariantId));
            var detailList = await AsyncExecuter.ToListAsync(detailsQuery);
            details = detailList.ToDictionary(d => d.EntityVariantId);
        }

        return rows
            .Select(r =>
            {
                details.TryGetValue(r.VariantId.GetValueOrDefault(), out var d);
                return new MetalVariantLookupDto
                {
                    CommodityId      = r.CommodityId,
                    MetalCode        = r.MetalCode,
                    MetalName        = r.MetalName,
                    VariantId        = r.VariantId,
                    VariantCode      = r.VariantCode,
                    VariantName      = r.VariantName,
                    IsMain           = r.IsMain,
                    IsQuantity       = r.IsQuantity,
                    StableQuantity   = r.StableQuantity,
                    LaborType        = d?.LaborType ?? MetalLaborType.Amount,
                    EntryLabor       = d?.EntryLabor ?? 0m,
                    EntryLaborUnitId = d?.EntryLaborUnitId,
                    ExitLabor        = d?.ExitLabor ?? 0m,
                    ExitLaborUnitId  = d?.ExitLaborUnitId,
                };
            })
            .OrderBy(x => x.MetalCode)
            .ThenBy(x => x.VariantCode)
            .ToList();
    }

    /// <summary>Madenin ürün projeksiyonu — iş <see cref="CommodityToProductProjector"/>'da; burada yalnız kaydı
    /// okuma + [Authorize] denetimi (mamüldeki <c>GoodAppService.ProjectToProductAsync</c> ile birebir simetrik).
    ///
    /// <para><b>Şekil <c>Family</c>'den okunur:</b> aile bu sınıfta ZATEN beyanlıdır; ikinci kez yazılsaydı
    /// iki beyan zamanla ayrışabilir ve projeksiyon sessizce yanlış kolu çalıştırırdı (connascence).</para></summary>
    public virtual async Task<ProductGetDto> ProjectToProductAsync(Guid metalId)
    {
        var entity = await Repository.FindAsync(metalId)
            ?? throw new BusinessException("TradeXpress:Metal:NotFound");

        return await CommodityToProduct.ProjectAsync(new CommodityProjectionSource(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            CommodityProjectionShapes.Of(Family)));
    }
}





