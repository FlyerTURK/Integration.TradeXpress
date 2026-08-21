using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Application;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Variants;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Integration.TradeXpress.Vouchers;

namespace Integration.TradeXpress.Jewelries;

/// <summary>
/// Jewelry (Mücevher) CRUD — company-scoped. Scalar alanlar + guard davranışı <see cref="HostCatalogCrudAppService{TEntity,
/// TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından; agnostik GRAF (doküman/not + nitelik/varyant
/// sistemi + varyant medyası) <see cref="CommodityAgnosticGraph"/>'a delege edilir (Good deseni; fiyat/stok uzantısı YOK —
/// mücevher fiyatı entity seviyesinde). Görünür = host(TenantId null) + çalışılan şirkete-özel; sıralama Code artan.
/// </summary>
[Authorize]
public class JewelryAppService
    : CommodityCatalogAppService<Jewelry, JewelryGetDto, JewelryListDto, JewelryListRequestDto, JewelryCreateDto, JewelryUpdateDto>,
      IJewelryAppService
{
    private const string JewelryEntityName = "Jewelry";
    private const string VariantImageEntityName = "JewelryVariant";   // varyant-özel medya/doküman/notun agnostik bağlam anahtarı

    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly CommodityAgnosticGraph _graph;

    public JewelryAppService(
        IRepository<Jewelry, Guid> repository,
        IRepository<EntityVariant, Guid> variantRepository,
        ICurrentCompany currentCompany,
        CommodityAgnosticGraph graph)
        : base(repository)
    {
        _jewelryRepository = repository;
        _variantRepository = variantRepository;
        _currentCompany = currentCompany;
        _graph = graph;
        LocalizationResource = typeof(TradeXpressResource);

        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Jewelries.Create;
        UpdatePolicyName = TradeXpressPermissions.Jewelries.Update;
        DeletePolicyName = TradeXpressPermissions.Jewelries.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    /// <summary>Reçete kullanım guard'ının aile anahtarı — CommodityId FK'sız snapshot olduğu için
    /// aile olmadan sorgu başka ailedeki aynı Guid'i yakalardı.</summary>
    /// <summary>Pasifleştirme geçişini tespit için — taban ortak IsActive arayüzü olmadığından tipli okuyamaz.</summary>
    protected override bool IsActiveOf(Jewelry entity)
    {
        return entity.IsActive;
    }

    protected override ProcessType Family
    {
        get { return ProcessType.Jewelry; }
    }

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Jewelry:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Jewelry:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Jewelry, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual async Task<List<JewelryListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        // Panel çalışılan şirketten farklı bir şirket verebilir → görünürlük o şirkete göre kurulur.
        // Global company filtresi working şirkete kilitli olduğundan bilinçli kapatılır;
        // görünürlüğü aşağıdaki predicate (istenen şirkete göre) zorlamaya devam eder.
        var scope = CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Jewelry>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            return await GetPickerListCoreAsync(scope);
        }
    }

    protected override Expression<Func<Jewelry, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Jewelry>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<Jewelry> ApplyFallbackSort(IQueryable<Jewelry> query, JewelryListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    protected override Task<Jewelry> MapToEntityAsync(JewelryCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        // SAHİPLİK client'tan DEĞİL aktif working company'den (fail-closed — bkz. CompanyOwnershipGuard).
        var entity = new Jewelry(
            createInput.Code, createInput.Name, CompanyOwnershipGuard.ResolveOwnerCompanyId(_currentCompany),
            createInput.IsQuantity, createInput.PriceByQuantity, createInput.PriceTypeChange,
            createInput.EntryPrice, createInput.EntryPriceUnitId, createInput.ExitPrice, createInput.ExitPriceUnitId);
        entity.SetAttributes(createInput.Model, createInput.Kind, createInput.Type, createInput.Color, createInput.Category, createInput.GroupCode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Jewelry entity)
    {
        // Update ile aynı scope/error-code — company-scoped: (TenantId, CompanyId, Code) unique index ile hizalı.
        return EnsureCodeUniqueAsync(
            entity, x => x.CompanyId == entity.CompanyId && x.Code == entity.Code,
            "TradeXpress:Jewelry:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(JewelryUpdateDto updateInput, Jewelry entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index
        // (TenantId, CompanyId, Code) ile hizalı — TenantId'yi standart filter verir.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Jewelry.Code), EntityFieldConsts.CodeMinLength, JewelryConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.Code == code,
            "TradeXpress:Jewelry:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetAttributes(updateInput.Model, updateInput.Kind, updateInput.Type, updateInput.Color, updateInput.Category, updateInput.GroupCode);
        entity.SetPricing(updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange,
                          updateInput.EntryPrice, updateInput.EntryPriceUnitId, updateInput.ExitPrice, updateInput.ExitPriceUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    protected override async Task EnrichListAsync(List<Jewelry> entities, List<JewelryListDto> dtos)
    {
        await base.EnrichListAsync(entities, dtos);

        // Grid önizlemesi: her mücevherin ANA varyantının varsayılan medyası (tek batch; N+1 yok).
        var previews = await _graph.GetVariantPreviewMapAsync(
            JewelryEntityName, VariantImageEntityName, dtos.Select(d => d.Id).ToList());
        foreach (var dto in dtos)
        {
            if (previews.TryGetValue(dto.Id, out var url))
            {
                dto.ImagePreviewUrl = url;
            }
        }
    }

    protected override Task EnrichPickerListAsync(List<Jewelry> entities, List<JewelryListDto> dtos)
    {
        // Picker (combo) görsel çizmez → önizleme batch sorgusunu ATLA (Metal deseni; base EnrichListAsync'e düşmesin).
        return Task.CompletedTask;
    }

    // ── Graf: Create/Update override → scalar save (base) + agnostik graf (görsel/doküman/not + nitelik/varyant). ──

    public override async Task<JewelryGetDto> CreateAsync(JewelryCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(dto.Id);
    }

    public override async Task<JewelryGetDto> UpdateAsync(Guid id, JewelryUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Documents, input.Notes, input.Attributes, input.Variants, input.Media);
        return await GetAsync(id);
    }

    private async Task SaveGraphAsync(
        Guid jewelryId, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<EntityVariantGraphDto> variants,
        List<EntityMediaLinkEditDto> media)
    {
        var jewelry = await _jewelryRepository.GetAsync(jewelryId);
        await _graph.SaveAsync(
            JewelryEntityName, VariantImageEntityName, jewelryId, jewelry.CompanyId, jewelry.Name,
            documents, notes, attributes, variants, media: media, ownerCode: jewelry.Code);
    }

    public override async Task<JewelryGetDto> GetAsync(Guid id)
    {
        var dto = await base.GetAsync(id);
        var graph = await _graph.LoadAsync(JewelryEntityName, VariantImageEntityName, id);
        dto.Media = graph.Media;
        dto.Documents = graph.Documents;
        dto.Notes = graph.Notes;
        dto.Attributes = graph.Attributes;
        dto.Variants = graph.Variants;
        return dto;
    }

    // Mücevher silinmeden ÖNCE (guard'lar geçti) — varyant grafı (+ varyant medyası) + doküman/not temizlenir.
    protected override Task BeforeDeleteAsync(Jewelry entity)
    {
        return _graph.DeleteAsync(JewelryEntityName, VariantImageEntityName, entity.Id);
    }

    // ── Varyant sistemi — jenerik agnostik servise delege (fiyat/stok uzantısı YOK; fiyat mücevherde). ──

    public virtual Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input)
    {
        return Task.FromResult(_graph.GenerateVariants(input));
    }

    public virtual Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid jewelryId)
    {
        return _graph.GetVariantPickerAsync(JewelryEntityName, jewelryId);
    }

    /// <summary>Reçete paneli için mücevher×varyant YASSI listesi — Good/Metal deseni. Fiyat MÜCEVHER seviyesinden
    /// (her varyant satırı aynı değeri taşır — varyantlar fiyatı paylaşır, bilinçli kısıt); seçim kimlik/stok
    /// içindir. Global filtreler kapatılıp görünürlük elle kurulur (host kaydı tenant filtresine takılmasın).</summary>
    public virtual async Task<List<CommodityVariantLookupDto>> GetVariantLookupAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var jewelryPredicate = CompanyScopedQueryable.CompanyOwnedVisiblePredicate<Jewelry>(CurrentTenant.Id, _currentCompany.Id);
            var variantPredicate = CompanyScopedQueryable.CompanyVisiblePredicate<EntityVariant>(CurrentTenant.Id, _currentCompany.Id);

            var rows = await AsyncExecuter.ToListAsync(
                from jewelry in (await Repository.GetQueryableAsync()).Where(jewelryPredicate)
                join variant in (await _variantRepository.GetQueryableAsync()).Where(variantPredicate) on jewelry.Id equals variant.EntityId
                where variant.EntityName == JewelryEntityName && !variant.IsDeleted && !jewelry.IsDeleted && variant.IsActive
                select new CommodityVariantLookupDto
                {
                    CommodityId = jewelry.Id,
                    CommodityCode = jewelry.Code,
                    CommodityName = jewelry.Name,
                    VariantId = variant.Id,
                    VariantCode = variant.Code,
                    VariantName = variant.Name,
                    IsMain = variant.IsMain,
                    IsQuantity = jewelry.IsQuantity,
                    PriceByQuantity = jewelry.PriceByQuantity,
                    EntryPrice = jewelry.EntryPrice,
                    EntryPriceUnitId = jewelry.EntryPriceUnitId,
                    ExitPrice = jewelry.ExitPrice,
                    ExitPriceUnitId = jewelry.ExitPriceUnitId,
                });

            return rows
                .OrderBy(x => x.CommodityCode)
                .ThenByDescending(x => x.IsMain)
                .ThenBy(x => x.VariantCode)
                .ToList();
        }
    }

    /// <summary>Mücevherin ürün projeksiyonu — iş <see cref="CommodityToProductProjector"/>'da; burada yalnız kaydı
    /// okuma + [Authorize] denetimi (mamüldeki <c>GoodAppService.ProjectToProductAsync</c> ile birebir simetrik).
    ///
    /// <para><b>Şekil <c>Family</c>'den okunur:</b> aile bu sınıfta ZATEN beyanlıdır; ikinci kez yazılsaydı
    /// iki beyan zamanla ayrışabilir ve projeksiyon sessizce yanlış kolu çalıştırırdı (connascence).</para></summary>
    public virtual async Task<ProductGetDto> ProjectToProductAsync(Guid jewelryId)
    {
        var entity = await Repository.FindAsync(jewelryId)
            ?? throw new BusinessException("TradeXpress:Jewelry:NotFound");

        return await CommodityToProduct.ProjectAsync(new CommodityProjectionSource(
            entity.Id,
            entity.Code,
            entity.Name,
            entity.Description,
            CommodityProjectionShapes.Of(Family)));
    }
}
