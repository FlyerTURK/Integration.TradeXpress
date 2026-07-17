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
using Volo.Abp.BlobStoring;
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

    private readonly IBlobContainer<MetalImagesContainer> _imageContainer;
    private readonly CommodityAgnosticGraph _graph;
    private readonly IRepository<MetalVariantDetail, Guid> _variantDetailRepository;
    private readonly IRepository<Integration.TradeXpress.Variants.EntityVariant, Guid> _entityVariantRepository;

    public MetalAppService(
        IRepository<Metal, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IBlobContainer<MetalImagesContainer> imageContainer,
        CommodityAgnosticGraph graph,
        IRepository<MetalVariantDetail, Guid> variantDetailRepository,
        IRepository<Integration.TradeXpress.Variants.EntityVariant, Guid> entityVariantRepository)
        : base(repository, unitRepository)
    {
        _imageContainer = imageContainer;
        _graph = graph;
        _variantDetailRepository = variantDetailRepository;
        _entityVariantRepository = entityVariantRepository;
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
        var entity = new Metal(
            createInput.Code, createInput.Name, createInput.FollowingUnitId!.Value,
            createInput.Factor, createInput.FactorChange,
            createInput.IsQuantity, createInput.StableQuantity);
        entity.SetBarcode(createInput.Barcode);
        entity.SetDescription(createInput.Description);
        entity.SetImage(MapImage(createInput.Image));
        return Task.FromResult(entity);
    }

    protected override Task EnsureCreateCodeUniqueAsync(Metal entity)
    {
        // Update ile aynı scope/error-code (TenantId bacağı standart filter'dan): aynı kod → dostane hata.
        return EnsureCodeUniqueAsync(
            entity, x => x.Code == entity.Code, "TradeXpress:Metal:CodeAlreadyExists", excludeSelf: false);
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
            code => x => x.Code == code,
            "TradeXpress:Metal:CodeAlreadyExists");

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(updateInput.FollowingUnitId!.Value);
        entity.SetFactor(updateInput.Factor);
        entity.SetFactorChange(updateInput.FactorChange);
        entity.SetQuantityTracking(updateInput.IsQuantity, updateInput.StableQuantity);
        entity.SetBarcode(updateInput.Barcode);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);

        // Yetim blob temizliği — değişim ÖNCESİ görsel saklanır, yeni görsel uygulandıktan sonra
        // artık referanssız kalan upload blob'u silinir (Product UpdateAsync deseni).
        var oldImage = entity.Image;
        entity.SetImage(MapImage(updateInput.Image));
        await DeleteOrphanImageBlobAsync(entity.TenantId, oldImage, entity.Image);
    }

    protected override async Task BeforeDeleteAsync(Metal entity)
    {
        // Guard'lar (policy + EnsureEditable) tabanda geçti — blob, yalnız gerçekten silinecek kayıt için temizlenir
        // (tenant'ın global kaydı silme denemesi hook'a hiç ulaşmaz).
        await DeleteOrphanImageBlobAsync(entity.TenantId, entity.Image, newImage: null);

        // Agnostik graf (varyant + varyant görselleri + doküman/not) temizliği — yetim önleme.
        await _graph.DeleteAsync(MetalEntityName, VariantImageEntityName, entity.Id);
    }

    protected override async Task EnrichGetAsync(Metal entity, MetalGetDto dto)
    {
        await base.EnrichGetAsync(entity, dto);

        // Client binding non-null model ister (paylaşılan SingleImageEditFields); görselsiz madende boş model döner.
        dto.Image ??= new MetalImageDto();

        if (entity.Image is { SourceType: ProductImageSourceType.Upload, BlobName: not null and not "" } image)
        {
            var thumbnail = await GetImageBlobOrNullAsync(
                entity.TenantId, ImageUploadPipeline.ThumbnailNameOf(image.BlobName));
            if (thumbnail is not null)
            {
                dto.Image.PreviewDataUrl = ImageUploadPipeline.BuildPreviewDataUrl(thumbnail);
            }
        }

        var graph = await _graph.LoadAsync(MetalEntityName, VariantImageEntityName, entity.Id);
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

        // Grid önizlemesi: Url tipinde doğrudan URL, Upload'da THUMBNAIL blobundan data-URL (Product listesiyle
        // aynı desen). Sayfa boyutu kadar satır işlenir (liste materialize edilip sayfalandıktan sonra çağrılır).
        for (var i = 0; i < dtos.Count; i++)
        {
            dtos[i].ImagePreviewUrl = await BuildPreviewUrlAsync(entities[i].TenantId, entities[i].Image);
        }
    }


    /// <summary>Liste önizleme URL'i — Url kaynağı doğrudan, Upload kaynağı thumbnail data-URL'i (yoksa null).</summary>
    private async Task<string?> BuildPreviewUrlAsync(Guid? ownerTenantId, MetalImage? image)
    {
        if (image is null)
        {
            return null;
        }

        if (image.SourceType == ProductImageSourceType.Url && !string.IsNullOrEmpty(image.Url))
        {
            return image.Url;
        }

        if (image.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(image.BlobName))
        {
            var thumbnail = await GetImageBlobOrNullAsync(
                ownerTenantId, ImageUploadPipeline.ThumbnailNameOf(image.BlobName));
            if (thumbnail is not null)
            {
                return ImageUploadPipeline.BuildPreviewDataUrl(thumbnail);
            }
        }

        return null;
    }

    /// <summary>
    /// Blob okuma, kaydın SAHİBİ tenant'ına sabitlenir (host kataloğu → host blob'u; tenant kaydı → tenant blob'u):
    /// (1) katalog host+tenant paylaşımlı ama blob container multi-tenant — tenant context'inde host blob'u
    /// bulunamazdı; (2) liste/picker çağrıları filter-disable scope'unda — container ada göre TÜM tenant'lar
    /// arasından rastgele çözülürdü. Filter yeniden AÇILIR + tenant kayda göre değiştirilir (review bulguları).
    /// </summary>
    private async Task<byte[]?> GetImageBlobOrNullAsync(Guid? ownerTenantId, string blobName)
    {
        using (DataFilter.Enable<IMultiTenant>())
        using (CurrentTenant.Change(ownerTenantId))
        {
            return await _imageContainer.GetAllBytesOrNullAsync(blobName);
        }
    }

    /// <summary>Görsel DTO'sunu owned tipe çevirir (normalize/eleme entity SetImage'da).</summary>
    private static MetalImage? MapImage(MetalImageDto? image)
    {
        if (image is null)
        {
            return null;
        }

        return new MetalImage(image.SourceType, image.Url, image.BlobName, image.FileName);
    }

    /// <summary>Eski upload blob'u yeni görselde artık kullanılmıyorsa ana blob + thumbnail'ini siler
    /// (Product DeleteOrphanImageBlobsAsync deseninin tek-görsel hali). Silme, okuma gibi kaydın sahibi
    /// tenant'ına sabitlenir — <see cref="GetImageBlobOrNullAsync"/> ile aynı gerekçe.</summary>
    private async Task DeleteOrphanImageBlobAsync(Guid? ownerTenantId, MetalImage? oldImage, MetalImage? newImage)
    {
        if (oldImage is not { SourceType: ProductImageSourceType.Upload, BlobName: not null and not "" })
        {
            return;
        }

        if (newImage is { SourceType: ProductImageSourceType.Upload }
            && string.Equals(newImage.BlobName, oldImage.BlobName, StringComparison.OrdinalIgnoreCase))
        {
            return;   // aynı blob korunuyor
        }

        using (DataFilter.Enable<IMultiTenant>())
        using (CurrentTenant.Change(ownerTenantId))
        {
            await _imageContainer.DeleteAsync(oldImage.BlobName);
            await _imageContainer.DeleteAsync(ImageUploadPipeline.ThumbnailNameOf(oldImage.BlobName));
        }
    }

    // ── Agnostik graf: Create/Update override → scalar save (base) + graf (doküman/not + nitelik/varyant). Fiyat/stok uzantısı YOK (maden fiyatı milyem/işçilik). ──

    public override async Task<MetalGetDto> CreateAsync(MetalCreateDto input)
    {
        var dto = await base.CreateAsync(input);
        await SaveGraphAsync(dto.Id, input.Name, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(dto.Id);
    }

    public override async Task<MetalGetDto> UpdateAsync(Guid id, MetalUpdateDto input)
    {
        var dto = await base.UpdateAsync(id, input);
        await SaveGraphAsync(id, input.Name, input.Documents, input.Notes, input.Attributes, input.Variants);
        return await GetAsync(id);
    }

    // Maden company-scoped DEĞİL → companyId null (varyant/nitelik tenant-geneli). Ana görsel OWNED tek (agnostik ana görsel yok).
    private Task SaveGraphAsync(
        Guid metalId, string ownerName, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<MetalVariantGraphDto> variants)
    {
        return _graph.SaveAsync(
            MetalEntityName, VariantImageEntityName, metalId, companyId: null, ownerName,
            documents, notes, attributes, variants,
            additionalSaveAction: (dto, variantId) => SaveVariantDetailAsync(null, (MetalVariantGraphDto)dto, variantId));
    }

    private async Task SaveVariantDetailAsync(Guid? companyId, MetalVariantGraphDto dto, Guid variantId)
    {
        var detail = await _variantDetailRepository.FirstOrDefaultAsync(x => x.EntityVariantId == variantId);
        var isNew = detail == null;

        if (isNew)
        {
            detail = new MetalVariantDetail(companyId, variantId);
        }

        detail.SetLabor(dto.LaborType, false, dto.EntryLabor, dto.EntryLaborUnitId, false, dto.ExitLabor, dto.ExitLaborUnitId, false, null);

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

    public virtual async Task<List<MetalVariantLookupDto>> GetVariantLookupAsync()
    {
        // Adım 1: Metal+Varyant listesi (soft-delete filtreli)
        var metalsQuery = await Repository.GetQueryableAsync();
        var variantsQuery = await _entityVariantRepository.GetQueryableAsync();

        var baseQuery = from metal in metalsQuery
                        join variant in variantsQuery on metal.Id equals variant.EntityId
                        where variant.EntityName == MetalEntityName && !variant.IsDeleted && !metal.IsDeleted
                        select new
                        {
                            CommodityId   = metal.Id,
                            MetalCode     = metal.Code,
                            MetalName     = metal.Name,
                            VariantId     = variant.Id,
                            VariantCode   = variant.Code,
                            VariantName   = variant.Name,
                            IsQuantity    = metal.IsQuantity,
                            StableQuantity = metal.StableQuantity
                        };

        var rows = await AsyncExecuter.ToListAsync(baseQuery);

        // Adım 2: MetalVariantDetail (CompanyScoped — ayrı sorgu, filter disable)
        Dictionary<Guid, MetalVariantDetail> details;
        using (DataFilter.Disable<ICompanyScoped>())
        {
            var variantIds = rows.Select(r => r.VariantId).ToList();
            var detailsQuery = (await _variantDetailRepository.GetQueryableAsync())
                .Where(d => variantIds.Contains(d.EntityVariantId));
            var detailList = await AsyncExecuter.ToListAsync(detailsQuery);
            details = detailList.ToDictionary(d => d.EntityVariantId);
        }

        return rows
            .Select(r =>
            {
                details.TryGetValue(r.VariantId, out var d);
                return new MetalVariantLookupDto
                {
                    CommodityId    = r.CommodityId,
                    MetalCode      = r.MetalCode,
                    MetalName      = r.MetalName,
                    VariantId      = r.VariantId,
                    VariantCode    = r.VariantCode,
                    VariantName    = r.VariantName,
                    IsQuantity     = r.IsQuantity,
                    StableQuantity = r.StableQuantity,
                    LaborType      = d?.LaborType ?? MetalLaborType.Amount,
                    EntryLabor     = d?.EntryLabor ?? 0m,
                    EntryLaborUnitId = d?.EntryLaborUnitId,
                    ExitLabor      = d?.ExitLabor ?? 0m,
                    ExitLaborUnitId  = d?.ExitLaborUnitId,
                };
            })
            .OrderBy(x => x.MetalCode)
            .ThenBy(x => x.VariantCode)
            .ToList();
    }
}





