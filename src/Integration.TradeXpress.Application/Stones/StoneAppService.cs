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

namespace Integration.TradeXpress.Stones;

/// <summary>
/// Stone (Taş) CRUD — company-scoped. Scalar alanlar + guard davranışı <see cref="HostCatalogCrudAppService{TEntity,
/// TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/> tabanından; agnostik GRAF (görsel/doküman/not + nitelik/varyant
/// sistemi + varyant görselleri) <see cref="CommodityAgnosticGraph"/>'a delege edilir (fiyat/stok uzantısı YOK — taş fiyatı
/// entity seviyesinde). Görünür = host(TenantId null) + çalışılan şirkete-özel; sıralama Code artan.
/// </summary>
[Authorize]
public class StoneAppService
    : HostCatalogCrudAppService<Stone, StoneGetDto, StoneListDto, StoneListRequestDto, StoneCreateDto, StoneUpdateDto>,
      IStoneAppService
{
    private const string StoneEntityName = "Stone";
    private const string VariantImageEntityName = "StoneVariant";   // varyant-özel görsellerin agnostik EntityImage anahtarı

    private readonly IRepository<Stone, Guid> _stoneRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly CommodityAgnosticGraph _graph;

    public StoneAppService(
        IRepository<Stone, Guid> repository,
        ICurrentCompany currentCompany,
        CommodityAgnosticGraph graph)
        : base(repository)
    {
        _stoneRepository = repository;
        _currentCompany = currentCompany;
        _graph = graph;
        LocalizationResource = typeof(TradeXpressResource);

        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): Metal deseniyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Stones.Create;
        UpdatePolicyName = TradeXpressPermissions.Stones.Update;
        DeletePolicyName = TradeXpressPermissions.Stones.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Stone:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Stone:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Stone, string>> PickerOrderSelector
    {
        get { return x => x.Code; }
    }

    public virtual async Task<List<StoneListDto>> GetPickerListAsync(Guid? companyId = null)
    {
        // Panel çalışılan şirketten farklı bir şirket verebilir → görünürlük o şirkete göre kurulur.
        // Global company filtresi working şirkete kilitli olduğundan bilinçli kapatılır;
        // görünürlüğü aşağıdaki predicate (istenen şirkete göre) zorlamaya devam eder.
        var scope = CompanyScopedQueryable.CompanyVisiblePredicate<Stone>(
            CurrentTenant.Id, companyId ?? _currentCompany.Id);

        using (DataFilter.Disable<ICompanyScoped>())
        {
            return await GetPickerListCoreAsync(scope);
        }
    }

    // Liste — base + agnostik varsayılan görsel thumbnail'i (tek batch, N+1 yok).
    public override async Task<PagedResultDto<StoneListDto>> GetListAsync(StoneListRequestDto input)
    {
        var page = await base.GetListAsync(input);
        await EnrichImagesAsync(page.Items);
        return page;
    }

    private async Task EnrichImagesAsync(IReadOnlyList<StoneListDto> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var previews = await _graph.GetImagePreviewMapAsync(StoneEntityName, items.Select(i => i.Id).ToList());
        foreach (var i in items)
        {
            i.ImagePreviewUrl = previews.GetValueOrDefault(i.Id);
        }
    }

    protected override Expression<Func<Stone, bool>> BuildVisibilityPredicate()
    {
        return CompanyScopedQueryable.CompanyVisiblePredicate<Stone>(CurrentTenant.Id, _currentCompany.Id);
    }

    protected override IQueryable<Stone> ApplyFallbackSort(IQueryable<Stone> query, StoneListRequestDto input)
    {
        if (HasExplicitSort(input))
        {
            return query;
        }

        return query.OrderBy(x => x.Code);
    }

    protected override Task<Stone> MapToEntityAsync(StoneCreateDto createInput)
    {
        // TenantId otomatik (host→null, tenant→kendi); zengin ctor + SetX.
        var entity = new Stone(
            createInput.Code, createInput.Name, createInput.CompanyId,
            createInput.IsQuantity, createInput.PriceByQuantity, createInput.PriceTypeChange,
            createInput.EntryPrice, createInput.EntryPriceUnitId, createInput.ExitPrice, createInput.ExitPriceUnitId);
        entity.SetAttributes(createInput.StoneKind, createInput.StoneType, createInput.Color, createInput.Cut,
                             createInput.Clarity, createInput.Sieve, createInput.Category, createInput.GroupCode);
        entity.SetDescription(createInput.Description);
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Stone entity)
    {
        // Update ile aynı scope/error-code — company-scoped: (TenantId, CompanyId, Code) unique index ile hizalı.
        return EnsureCodeUniqueAsync(
            entity, x => x.CompanyId == entity.CompanyId && x.Code == entity.Code,
            "TradeXpress:Stone:CodeAlreadyExists", excludeSelf: false);
    }

    protected override async Task MapToEntityAsync(StoneUpdateDto updateInput, Stone entity)
    {
        // Kod düzenlenebilir (ürün kuralı 2026-07-04); benzersizlik scope'u DB unique index
        // (TenantId, CompanyId, Code) ile hizalı — TenantId'yi standart filter verir.
        await ApplyCodeChangeAsync(
            entity,
            updateInput.Code,
            raw => StringFieldGuard.NormalizeCode(
                raw, nameof(Stone.Code), EntityFieldConsts.CodeMinLength, StoneConsts.CodeMaxLength),
            e => e.Code,
            (e, code) => e.SetCode(code),
            code => x => x.CompanyId == entity.CompanyId && x.Code == code,
            "TradeXpress:Stone:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetAttributes(updateInput.StoneKind, updateInput.StoneType, updateInput.Color, updateInput.Cut,
                             updateInput.Clarity, updateInput.Sieve, updateInput.Category, updateInput.GroupCode);
        entity.SetPricing(updateInput.IsQuantity, updateInput.PriceByQuantity, updateInput.PriceTypeChange,
                          updateInput.EntryPrice, updateInput.EntryPriceUnitId, updateInput.ExitPrice, updateInput.ExitPriceUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    // ── Graf: Create/Update override → scalar save (base) + agnostik graf (görsel/doküman/not + nitelik/varyant). ──

    public override async Task<StoneGetDto> CreateAsync(StoneCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Images, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(dto.Id);
    }

    public override async Task<StoneGetDto> UpdateAsync(Guid id, StoneUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Images, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(id);
    }

    private async Task SaveGraphAsync(
        Guid stoneId, List<EntityImageEditDto> images, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<EntityVariantGraphDto> variants)
    {
        var stone = await _stoneRepository.GetAsync(stoneId);
        await _graph.SaveAsync(
            StoneEntityName, VariantImageEntityName, stoneId, stone.CompanyId, stone.Name,
            images, documents, notes, attributes, variants);
    }

    public override async Task<StoneGetDto> GetAsync(Guid id)
    {
        var dto = await base.GetAsync(id);
        var graph = await _graph.LoadAsync(StoneEntityName, VariantImageEntityName, id);
        dto.Images = graph.Images;
        dto.Documents = graph.Documents;
        dto.Notes = graph.Notes;
        dto.Attributes = graph.Attributes;
        dto.Variants = graph.Variants;
        return dto;
    }

    // Taş silinmeden ÖNCE (guard'lar geçti) — varyant grafı (+ varyant görselleri) + görsel/doküman/not temizlenir.
    protected override Task BeforeDeleteAsync(Stone entity)
    {
        return _graph.DeleteAsync(StoneEntityName, VariantImageEntityName, entity.Id);
    }

    // ── Varyant sistemi — jenerik agnostik servise delege (fiyat/stok uzantısı YOK; fiyat taşta). ──

    public virtual Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input)
    {
        return Task.FromResult(_graph.GenerateVariants(input));
    }

    public virtual Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid stoneId)
    {
        return _graph.GetVariantPickerAsync(StoneEntityName, stoneId);
    }
}
