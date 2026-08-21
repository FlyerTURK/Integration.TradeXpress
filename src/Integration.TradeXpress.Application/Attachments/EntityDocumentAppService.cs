using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik doküman servisi — herhangi bir kaydın (EntityName + EntityId) doküman setini yönetir. TEK container
/// (<see cref="EntityDocumentsContainer"/>), ham blob (görsel-özel thumbnail/pipeline YOK). Kaydetme: parent AppService
/// kayıt sonrası <see cref="ReplaceForAsync"/> ile bu tabloyu değiştirir (graph-save); kaldırılan dokümanların blob'u
/// temizlenir (orphan yok). Download client'tan çağrılabilsin diye remote (in-process Blazor Server'da da geçerli).
/// </summary>
[Authorize]
[RemoteService]
public class EntityDocumentAppService : TradeXpressAppService, IEntityDocumentAppService
{
    private const string ErrorCodePrefix = "TradeXpress:Document";

    private readonly IRepository<EntityDocument, Guid> _repository;
    private readonly IBlobContainer<EntityDocumentsContainer> _container;

    public EntityDocumentAppService(
        IRepository<EntityDocument, Guid> repository,
        IBlobContainer<EntityDocumentsContainer> container)
    {
        _repository = repository;
        _container = container;
    }

    public virtual async Task<List<EntityDocumentDto>> GetForAsync(string entityName, Guid entityId)
    {
        var rows = await LoadOrderedAsync(entityName, entityId);
        return rows.Select(row => new EntityDocumentDto
        {
            Id = row.Id,
            FileName = row.FileName,
            BlobName = row.BlobName,
            ContentType = row.ContentType,
            Size = row.Size,
            Description = row.Description,
            DisplayOrder = row.DisplayOrder,
        }).ToList();
    }

    public virtual async Task ReplaceForAsync(string entityName, Guid entityId, List<EntityDocumentEditDto> documents)
    {
        var en = (entityName ?? string.Empty).Trim();
        var normalized = Normalize(documents);
        var existing = await LoadOrderedAsync(en, entityId);

        // Kaldırılan dokümanların blob'unu temizle (yeni sette olmayan BlobName'ler).
        var keptBlobs = normalized
            .Select(d => d.BlobName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in existing)
        {
            if (!keptBlobs.Contains(old.BlobName))
            {
                await _container.DeleteAsync(old.BlobName);
            }
        }

        // Delete-all + insert-new (doküman setinin stabil kimliğe ihtiyacı yok — sipariş satırı deseni).
        // FLUSH ZORUNLU (2026-08-20, ProductCommodityProjectionTests'te yakalandı): "replace" sözleşmesi çağrıya döndüğünde setin
        // SORGULANABİLİR olmasını gerektirir. autoSave:false ile yazıldığında satırlar yalnız değişiklik
        // izleyicisinde durur ve AYNI UoW içindeki bir SORGU onları GÖRMEZ (EF sorgu öncesi kendiliğinden
        // flush etmez). Bedeli sessizdi: CreateAsync/UpdateAsync sonunda GetAsync ile geri okunan DTO'da
        // SON yazılan setin satırları EKSİK geliyordu — kaydeden kullanıcı görselini/dokümanını kaybolmuş
        // sanıyor, yalnız sayfayı yenileyince geri geliyordu. Önceki satırların kurtulması TESADÜFTÜ: araya
        // giren başka bir autoSave:true çağrısı onları flush ediyordu, sonuncuyu edecek kimse yoktu.
        // Silme ÖNCE flush edilir (aynı anahtarı yeniden ekleyen replace tek SaveChanges'te çakışmasın).
        await _repository.DeleteManyAsync(existing, autoSave: true);

        var replacements = normalized
            .Select(doc => new EntityDocument(
                en, entityId, doc.FileName!, doc.BlobName!, doc.ContentType ?? DefaultContentType,
                doc.Size, doc.Description, doc.DisplayOrder))
            .ToList();
        await _repository.InsertManyAsync(replacements, autoSave: true);
    }

    public virtual async Task<EntityDocumentUploadResultDto> UploadAsync(EntityDocumentUploadDto input)
    {
        var content = input.Content ?? Array.Empty<byte>();
        if (content.Length == 0)
        {
            throw new BusinessException(ErrorCodePrefix + ":Empty");
        }

        if (content.Length > EntityDocumentConsts.MaxDocumentSizeBytes)
        {
            throw new BusinessException(ErrorCodePrefix + ":TooLarge")
                .WithData("MaxMb", EntityDocumentConsts.MaxDocumentSizeBytes / (1024 * 1024));
        }

        var extension = Path.GetExtension(input.FileName).ToLowerInvariant();
        var blobName = GuidGenerator.Create().ToString("N") + extension;
        await _container.SaveAsync(blobName, content);

        return new EntityDocumentUploadResultDto
        {
            BlobName = blobName,
            ContentType = ResolveContentType(extension),
            Size = content.Length,
        };
    }

    public virtual async Task<EntityDocumentDownloadDto> DownloadAsync(Guid id)
    {
        var document = await _repository.GetAsync(id);
        var content = await _container.GetAllBytesAsync(document.BlobName);
        return new EntityDocumentDownloadDto
        {
            FileName = document.FileName,
            ContentType = document.ContentType,
            Content = content,
        };
    }

    private async Task<List<EntityDocument>> LoadOrderedAsync(string entityName, Guid entityId)
    {
        var en = (entityName ?? string.Empty).Trim();
        return await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == en && x.EntityId == entityId)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id));
    }

    // Blob'u olmayan (yarım) satırları ele + aynı blob iki kez giremez (ilk kalır) + DisplayOrder'a göre sırala/yeniden numaralandır.
    private static List<EntityDocumentEditDto> Normalize(List<EntityDocumentEditDto>? documents)
    {
        var list = (documents ?? new List<EntityDocumentEditDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.BlobName) && !string.IsNullOrWhiteSpace(d.FileName))
            .OrderBy(d => d.DisplayOrder)
            .ToList();

        var seenBlob = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<EntityDocumentEditDto>();
        foreach (var d in list)
        {
            if (!seenBlob.Add(d.BlobName!))
            {
                continue;
            }

            deduped.Add(d);
        }

        for (var idx = 0; idx < deduped.Count; idx++)
        {
            deduped[idx].DisplayOrder = idx;
        }

        return deduped;
    }

    private const string DefaultContentType = "application/octet-stream";

    private static string ResolveContentType(string extension)
    {
        return ContentTypes.GetValueOrDefault(extension, DefaultContentType);
    }

    // Yaygın doküman uzantı → MIME eşlemesi; bilinmeyen = octet-stream (indirmeye engel değil).
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".rtf"] = "application/rtf",
        [".xml"] = "application/xml",
        [".json"] = "application/json",
        [".zip"] = "application/zip",
        [".rar"] = "application/vnd.rar",
        [".7z"] = "application/x-7z-compressed",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };
}
