using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Commodities;

/// <summary>
/// Emtia (Jewelry/Stone/Metal…) kartlarının AGNOSTİK graf orkestrasyonu — görsel + doküman + not + varyant sistemi
/// (nitelik/değer kartezyeni → çekirdek varyantlar) + varyant-özel görseller. Sahip AppService yalnız birkaç satır
/// delege eder (DRY): Save / Load / Delete + varyant picker + liste görsel önizlemesi.
/// <para>Good AYRI orkestrasyon tutar (kendi tedarikçi drill'i + varyant-başı fiyat uzantısı GoodVariantDetail olduğundan);
/// bu yardımcı fiyat-uzantısı OLMAYAN emtialar içindir (fiyat entity seviyesinde kalır).</para>
/// </summary>
public class CommodityAgnosticGraph : ITransientDependency
{
    private readonly IEntityImageAppService _images;
    private readonly IEntityDocumentAppService _documents;
    private readonly IEntityNoteAppService _notes;
    private readonly IEntityVariantGraphService _variants;

    public CommodityAgnosticGraph(
        IEntityImageAppService images,
        IEntityDocumentAppService documents,
        IEntityNoteAppService notes,
        IEntityVariantGraphService variants)
    {
        _images = images;
        _documents = documents;
        _notes = notes;
        _variants = variants;
    }

    /// <summary>Grafı saklar (sahip entity zaten kaydedilmiş olmalı): görsel/doküman/not replace-all + varyant çekirdeği
    /// (nitelik/değer → synchronizer kartezyen) + her varyantın KENDİ görselleri (<paramref name="variantImageEntityName"/> bağlamı).</summary>
    public async Task SaveAsync(
        string entityName, string variantImageEntityName, Guid entityId, Guid? companyId, string ownerName,
        List<EntityImageEditDto> images, List<EntityDocumentEditDto> documents, List<EntityNoteEditDto> notes,
        List<EntityAttributeGraphDto> attributes, IReadOnlyList<EntityVariantGraphDto> variants)
    {
        await _images.ReplaceForAsync(entityName, entityId, images);
        await _documents.ReplaceForAsync(entityName, entityId, documents);
        await _notes.ReplaceForAsync(entityName, entityId, notes);
        await _variants.SaveGraphAsync(
            entityName, entityId, companyId, ownerName, attributes, variants,
            saveExtensionAsync: (dto, variantId) => _images.ReplaceForAsync(variantImageEntityName, variantId, dto.Images));
    }

    /// <summary>Grafı okur (GetAsync projeksiyonu): görsel/doküman/not (Edit DTO'ları) + varyant grafı (her varyant KENDİ görselleriyle).</summary>
    public async Task<CommodityGraphData> LoadAsync(string entityName, string variantImageEntityName, Guid entityId)
    {
        var data = new CommodityGraphData
        {
            Images = (await _images.GetForAsync(entityName, entityId)).Select(ToImageEdit).ToList(),
            Documents = (await _documents.GetForAsync(entityName, entityId)).Select(ToDocumentEdit).ToList(),
            Notes = (await _notes.GetForAsync(entityName, entityId)).Select(ToNoteEdit).ToList(),
        };

        var graph = await _variants.LoadGraphAsync(entityName, entityId);
        data.Attributes = graph.Attributes;
        foreach (var v in graph.Variants)
        {
            v.Images = (await _images.GetForAsync(variantImageEntityName, v.Id)).Select(ToImageEdit).ToList();
            data.Variants.Add(v);
        }

        return data;
    }

    /// <summary>Sahip entity silinmeden ÖNCE: varyant grafı (+ varyant görselleri) + görsel/doküman/not temizliği (yetim önleme).</summary>
    public async Task DeleteAsync(string entityName, string variantImageEntityName, Guid entityId)
    {
        await _variants.DeleteForAsync(
            entityName, entityId,
            deleteExtensionAsync: async ids =>
            {
                foreach (var vid in ids)
                {
                    await _images.ReplaceForAsync(variantImageEntityName, vid, new List<EntityImageEditDto>());
                }
            });

        await _images.ReplaceForAsync(entityName, entityId, new List<EntityImageEditDto>());
        await _documents.ReplaceForAsync(entityName, entityId, new List<EntityDocumentEditDto>());
        await _notes.ReplaceForAsync(entityName, entityId, new List<EntityNoteEditDto>());
    }

    /// <summary>Persistsiz varyant önizlemesi (nitelik×değer kartezyeni) — jenerik servise delege (DB'ye YAZMAZ). Sahip AppService
    /// "Varyantları Oluştur" akışında çağırır; fiyat uzantısı olmayan emtiada çekirdek DTO doğrudan kullanılır (re-project YOK).</summary>
    public List<EntityVariantGraphDto> GenerateVariants(EntityVariantGenerateRequestDto input)
    {
        return _variants.GenerateVariants(input);
    }

    /// <summary>Fiş satırı panelinin varyant combo'su — AKTİF varyantlar (fiyatsız; fiyat entity seviyesinde). Ana varyant öncelikli.</summary>
    public Task<List<CommodityVariantOptionDto>> GetVariantPickerAsync(string entityName, Guid entityId)
    {
        return _variants.GetActiveVariantOptionsAsync(entityName, entityId);
    }

    /// <summary>Liste grid thumbnail'leri — varsayılan görsel önizlemeleri (tek batch; N+1 yok).</summary>
    public Task<Dictionary<Guid, string?>> GetImagePreviewMapAsync(string entityName, IReadOnlyCollection<Guid> ids)
    {
        return _images.GetDefaultPreviewMapAsync(entityName, ids);
    }

    private static EntityImageEditDto ToImageEdit(EntityImageDto i)
    {
        return new EntityImageEditDto
        {
            SourceType = i.SourceType,
            Url = i.Url,
            BlobName = i.BlobName,
            FileName = i.FileName,
            DisplayOrder = i.DisplayOrder,
            IsDefault = i.IsDefault,
            PreviewDataUrl = i.PreviewDataUrl,
        };
    }

    private static EntityDocumentEditDto ToDocumentEdit(EntityDocumentDto d)
    {
        return new EntityDocumentEditDto
        {
            Id = d.Id,
            FileName = d.FileName,
            BlobName = d.BlobName,
            ContentType = d.ContentType,
            Size = d.Size,
            Description = d.Description,
            DisplayOrder = d.DisplayOrder,
        };
    }

    private static EntityNoteEditDto ToNoteEdit(EntityNoteDto n)
    {
        return new EntityNoteEditDto
        {
            Id = n.Id,
            Title = n.Title,
            Text = n.Text,
            DisplayOrder = n.DisplayOrder,
            CreationTime = n.CreationTime,
        };
    }
}

/// <summary>Bir emtianın okunan agnostik grafı — sahip GetDto'ya kopyalanır (Images/Documents/Notes/Attributes/Variants).</summary>
public class CommodityGraphData
{
    public List<EntityImageEditDto> Images { get; set; } = new();
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<EntityVariantGraphDto> Variants { get; set; } = new();
}
