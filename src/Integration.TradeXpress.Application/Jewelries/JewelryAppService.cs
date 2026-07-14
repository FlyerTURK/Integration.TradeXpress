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
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Jewelries;

/// <summary>
/// Jewelry (Mücevher) CRUD — company-scoped. Scalar alanlar + guard davranışı <see cref="HostCatalogCrudAppService{TEntity,
/// TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından; agnostik GRAF (görsel/doküman/not + nitelik/varyant
/// sistemi + varyant görselleri) <see cref="CommodityAgnosticGraph"/>'a delege edilir (Good deseni; fiyat/stok uzantısı YOK —
/// mücevher fiyatı entity seviyesinde). Görünür = host(TenantId null) + çalışılan şirkete-özel; sıralama Code artan.
/// </summary>
[Authorize]
public class JewelryAppService
    : HostCatalogCrudAppService<Jewelry, JewelryGetDto, JewelryListDto, JewelryListRequestDto, JewelryCreateDto, JewelryUpdateDto>,
      IJewelryAppService
{
    private const string JewelryEntityName = "Jewelry";
    private const string VariantImageEntityName = "JewelryVariant";   // varyant-özel görsellerin agnostik EntityImage anahtarı

    private readonly IRepository<Jewelry, Guid> _jewelryRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly CommodityAgnosticGraph _graph;

    public JewelryAppService(
        IRepository<Jewelry, Guid> repository,
        ICurrentCompany currentCompany,
        CommodityAgnosticGraph graph)
        : base(repository)
    {
        _jewelryRepository = repository;
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
        var scope = CompanyScopedQueryable.CompanyVisiblePredicate<Jewelry>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            return await GetPickerListCoreAsync(scope);
        }
    }

    // Liste — base + agnostik varsayılan görsel thumbnail'i (Good deseniyle aynı; tek batch, N+1 yok).
    public override async Task<PagedResultDto<JewelryListDto>> GetListAsync(JewelryListRequestDto input)
    {
        var page = await base.GetListAsync(input);
        await EnrichImagesAsync(page.Items);
        return page;
    }

    private async Task EnrichImagesAsync(IReadOnlyList<JewelryListDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var previews = await _graph.GetImagePreviewMapAsync(JewelryEntityName, items.Select(i => i.Id).ToList());
        foreach (var i in items)
        {
            i.ImagePreviewUrl = previews.GetValueOrDefault(i.Id);
        }
    }

    protected override Expression<Func<Jewelry, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyVisiblePredicate<Jewelry>(CurrentTenant.Id, _currentCompany.Id);
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
        var entity = new Jewelry(
            createInput.Code, createInput.Name, createInput.CompanyId,
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

    // ── Graf: Create/Update override → scalar save (base) + agnostik graf (görsel/doküman/not + nitelik/varyant). ──

    public override async Task<JewelryGetDto> CreateAsync(JewelryCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Images, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(dto.Id);
    }

    public override async Task<JewelryGetDto> UpdateAsync(Guid id, JewelryUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Images, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(id);
    }

    private async Task SaveGraphAsync(
        Guid jewelryId, List<EntityImageEditDto> images, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<EntityVariantGraphDto> variants)
    {
        var jewelry = await _jewelryRepository.GetAsync(jewelryId);
        await _graph.SaveAsync(
            JewelryEntityName, VariantImageEntityName, jewelryId, jewelry.CompanyId, jewelry.Name,
            images, documents, notes, attributes, variants);
    }

    public override async Task<JewelryGetDto> GetAsync(Guid id)
    {
        var dto = await base.GetAsync(id);
        var graph = await _graph.LoadAsync(JewelryEntityName, VariantImageEntityName, id);
        dto.Images = graph.Images;
        dto.Documents = graph.Documents;
        dto.Notes = graph.Notes;
        dto.Attributes = graph.Attributes;
        dto.Variants = graph.Variants;
        return dto;
    }

    // Mücevher silinmeden ÖNCE (guard'lar geçti) — varyant grafı (+ varyant görselleri) + görsel/doküman/not temizlenir.
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
}
