using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Commodities;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.Variants;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

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
    private const string VariantImageEntityName = "MetalVariant";   // varyant-özel görsellerin agnostik EntityImage anahtarı

    private readonly IBlobContainer<MetalImagesContainer> _imageContainer;
    private readonly CommodityAgnosticGraph _graph;

    public MetalAppService(
        IRepository<Metal, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IBlobContainer<MetalImagesContainer> imageContainer,
        CommodityAgnosticGraph graph)
        : base(repository, unitRepository)
    {
        _imageContainer = imageContainer;
        _graph = graph;
        // Katalog yönetimi izinli (okuma/liste serbest — [Authorize] yeter): combo ✎/+ görünürlüğüyle hizalı.
        CreatePolicyName = TradeXpressPermissions.Metals.Create;
        UpdatePolicyName = TradeXpressPermissions.Metals.Update;
        DeletePolicyName = TradeXpressPermissions.Metals.Delete;
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
            createInput.IsQuantity, createInput.StableQuantity,
            createInput.LaborType, createInput.LaborTypeChange,
            createInput.EntryLabor, createInput.EntryLaborUnitId, createInput.EntryLaborChange,
            createInput.ExitLabor, createInput.ExitLaborUnitId, createInput.ExitLaborChange,
            createInput.CostUnitId);
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
        entity.SetLabor(
            updateInput.LaborType, updateInput.LaborTypeChange,
            updateInput.EntryLabor, updateInput.EntryLaborUnitId, updateInput.EntryLaborChange,
            updateInput.ExitLabor, updateInput.ExitLaborUnitId, updateInput.ExitLaborChange,
            updateInput.CostUnitId);
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

        // Agnostik graf (Doküman/Not + Nitelik/Varyant + varyant görselleri) — ANA görsel OWNED tek olduğundan graph.Images YOKSAYILIR.
        var graph = await _graph.LoadAsync(MetalEntityName, VariantImageEntityName, entity.Id);
        dto.Documents = graph.Documents;
        dto.Notes = graph.Notes;
        dto.Attributes = graph.Attributes;
        dto.Variants = graph.Variants;
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

    protected override Task EnrichPickerListAsync(List<Metal> entities, List<MetalListDto> dtos)
    {
        // Picker (combo) görsel çizmez ve sık çağrılır — satır başına blob sorgusu (N+1) + base64 payload'ı
        // circuit'e taşımamak için görsel zenginleştirmesi ATLANIR (review bulgusu; FollowingUnitCode tabanda dolar).
        return Task.CompletedTask;
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

    // Maden company-scoped DEĞİL → companyId null (varyant/nitelik tenant-geneli). Ana görsel OWNED tek → agnostik ana görsel BOŞ geçilir.
    private Task SaveGraphAsync(
        Guid metalId, string ownerName, List<EntityDocumentEditDto> documents,
        List<EntityNoteEditDto> notes, List<EntityAttributeGraphDto> attributes, List<EntityVariantGraphDto> variants)
    {
        return _graph.SaveAsync(
            MetalEntityName, VariantImageEntityName, metalId, companyId: null, ownerName,
            new List<EntityImageEditDto>(), documents, notes, attributes, variants);
    }

    public virtual Task<List<EntityVariantGraphDto>> GenerateVariantsAsync(EntityVariantGenerateRequestDto input)
    {
        return Task.FromResult(_graph.GenerateVariants(input));
    }

    public virtual Task<List<CommodityVariantOptionDto>> GetVariantPickerListAsync(Guid metalId)
    {
        return _graph.GetVariantPickerAsync(MetalEntityName, metalId);
    }
}
