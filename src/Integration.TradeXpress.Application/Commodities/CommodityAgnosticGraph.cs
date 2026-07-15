using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Integration.TradeXpress.Variants;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Commodities;

/// <summary>
/// Emtia (Jewelry/Stone/Metal…) kartlarının AGNOSTİK graf orkestrasyonu — doküman + not + varyant sistemi
/// (nitelik/değer kartezyeni → çekirdek varyantlar) + varyant-özel MEDYA. Sahip AppService yalnız birkaç satır
/// delege eder (DRY): Save / Load / Delete + varyant picker.
/// <para>Good AYRI orkestrasyon tutar (kendi tedarikçi drill'i + varyant-başı fiyat uzantısı GoodVariantDetail olduğundan);
/// bu yardımcı fiyat-uzantısı OLMAYAN emtialar içindir (fiyat entity seviyesinde kalır).</para>
/// </summary>
public class CommodityAgnosticGraph : ITransientDependency
{
    private readonly IEntityDocumentAppService _documents;
    private readonly IEntityNoteAppService _notes;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IEntityVariantGraphService _variants;

    public CommodityAgnosticGraph(
        IEntityDocumentAppService documents,
        IEntityNoteAppService notes,
        IEntityMediaAppService entityMedia,
        IEntityVariantGraphService variants)
    {
        _documents = documents;
        _notes = notes;
        _entityMedia = entityMedia;
        _variants = variants;
    }

    /// <summary>Grafı saklar (sahip entity zaten kaydedilmiş olmalı): doküman/not replace-all + varyant çekirdeği
    /// (nitelik/değer → synchronizer kartezyen) + her varyantın KENDİ medyası (<paramref name="variantImageEntityName"/> bağlamı).</summary>
    public async Task SaveAsync(
        string entityName, string variantImageEntityName, Guid entityId, Guid? companyId, string ownerName,
        List<EntityDocumentEditDto> documents, List<EntityNoteEditDto> notes,
        List<EntityAttributeGraphDto> attributes, IReadOnlyList<EntityVariantGraphDto> variants)
    {
        await _documents.ReplaceForAsync(entityName, entityId, documents);
        await _notes.ReplaceForAsync(entityName, entityId, notes);
        await _variants.SaveGraphAsync(
            entityName, entityId, companyId, ownerName, attributes, variants,
            saveExtensionAsync: async (dto, variantId) =>
            {
                // Varyant-özel ekler AYNI varyant bağlamıyla (variantImageEntityName): MEDYA (görsel+video link) + doküman + not.
                await _entityMedia.ReplaceForAsync(variantImageEntityName, variantId, companyId, dto.Media);
                await _documents.ReplaceForAsync(variantImageEntityName, variantId, dto.Documents);
                await _notes.ReplaceForAsync(variantImageEntityName, variantId, dto.Notes);
            });
    }

    /// <summary>Grafı okur (GetAsync projeksiyonu): doküman/not (Edit DTO'ları) + varyant grafı (her varyant KENDİ medyasıyla).</summary>
    public async Task<CommodityGraphData> LoadAsync(string entityName, string variantImageEntityName, Guid entityId)
    {
        var data = new CommodityGraphData
        {
            Documents = (await _documents.GetForAsync(entityName, entityId)).Select(ToDocumentEdit).ToList(),
            Notes = (await _notes.GetForAsync(entityName, entityId)).Select(ToNoteEdit).ToList(),
        };

        var graph = await _variants.LoadGraphAsync(entityName, entityId);
        data.Attributes = graph.Attributes;
        foreach (var v in graph.Variants)
        {
            v.Media = await _entityMedia.GetForAsync(variantImageEntityName, v.Id);
            v.Documents = (await _documents.GetForAsync(variantImageEntityName, v.Id)).Select(ToDocumentEdit).ToList();
            v.Notes = (await _notes.GetForAsync(variantImageEntityName, v.Id)).Select(ToNoteEdit).ToList();
            data.Variants.Add(v);
        }

        return data;
    }

    /// <summary>Sahip entity silinmeden ÖNCE: varyant grafı (+ varyant medyası) + doküman/not temizliği (yetim önleme).</summary>
    public async Task DeleteAsync(string entityName, string variantImageEntityName, Guid entityId)
    {
        await _variants.DeleteForAsync(
            entityName, entityId,
            deleteExtensionAsync: async ids =>
            {
                foreach (var vid in ids)
                {
                    await _entityMedia.ReplaceForAsync(variantImageEntityName, vid, null, new List<EntityMediaLinkEditDto>());
                    await _documents.ReplaceForAsync(variantImageEntityName, vid, new List<EntityDocumentEditDto>());
                    await _notes.ReplaceForAsync(variantImageEntityName, vid, new List<EntityNoteEditDto>());
                }
            });

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

    /// <summary>Liste grid thumbnail'leri — her sahip kaydın ANA varyantının VARSAYILAN medyasının poster'ı (tek batch; N+1 yok).</summary>
    public async Task<Dictionary<Guid, string?>> GetVariantPreviewMapAsync(
        string entityName, string variantImageEntityName, IReadOnlyCollection<Guid> entityIds)
    {
        var mainVariants = await _variants.GetMainVariantMapAsync(entityName, entityIds);
        if (mainVariants.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var posters = await _entityMedia.GetDefaultPosterMapAsync(variantImageEntityName, mainVariants.Values.ToList());
        var result = new Dictionary<Guid, string?>();
        foreach (var kv in mainVariants)
        {
            result[kv.Key] = posters.TryGetValue(kv.Value, out var url) ? url : null;
        }

        return result;
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

/// <summary>Bir emtianın okunan agnostik grafı — sahip GetDto'ya kopyalanır (Documents/Notes/Attributes/Variants).</summary>
public class CommodityGraphData
{
    public List<EntityDocumentEditDto> Documents { get; set; } = new();
    public List<EntityNoteEditDto> Notes { get; set; } = new();
    public List<EntityAttributeGraphDto> Attributes { get; set; } = new();
    public List<EntityVariantGraphDto> Variants { get; set; } = new();
}
