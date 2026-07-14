using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik görsel servisi — herhangi bir kaydın (EntityName + EntityId) görsel setini yönetir. Ortak
/// <see cref="ImageUploadPipeline"/> (guard + thumbnail + blob) + TEK container (<see cref="EntityImagesContainer"/>).
/// Kaydetme: parent AppService kayıt sonrası <see cref="ReplaceForAsync"/> ile bu tabloyu değiştirir (graph-save);
/// kaldırılan yüklenmiş görsellerin blob + thumbnail'i temizlenir (orphan yok).
/// </summary>
[Authorize]
public class EntityImageAppService : TradeXpressAppService, IEntityImageAppService
{
    private const string ErrorCodePrefix = "TradeXpress:Image";

    private readonly IRepository<EntityImage, Guid> _repository;
    private readonly IBlobContainer<EntityImagesContainer> _container;

    public EntityImageAppService(
        IRepository<EntityImage, Guid> repository,
        IBlobContainer<EntityImagesContainer> container)
    {
        _repository = repository;
        _container = container;
    }

    public virtual async Task<List<EntityImageDto>> GetForAsync(string entityName, Guid entityId)
    {
        var rows = await LoadOrderedAsync(entityName, entityId);
        var result = new List<EntityImageDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(new EntityImageDto
            {
                Id = row.Id,
                SourceType = row.SourceType,
                Url = row.Url,
                BlobName = row.BlobName,
                FileName = row.FileName,
                DisplayOrder = row.DisplayOrder,
                IsDefault = row.IsDefault,
                PreviewDataUrl = await BuildPreviewAsync(row.SourceType, row.Url, row.BlobName),
            });
        }

        return result;
    }

    public virtual async Task ReplaceForAsync(string entityName, Guid entityId, List<EntityImageEditDto> images)
    {
        var en = (entityName ?? string.Empty).Trim();
        var normalized = Normalize(images);
        var existing = await LoadOrderedAsync(en, entityId);

        // Kaldırılan YÜKLENMİŞ görsellerin blob + thumbnail'ini temizle (yeni sette olmayan BlobName'ler).
        var keptBlobs = normalized
            .Where(i => i.SourceType == ProductImageSourceType.Upload && !string.IsNullOrWhiteSpace(i.BlobName))
            .Select(i => i.BlobName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in existing)
        {
            if (old.SourceType == ProductImageSourceType.Upload && old.BlobName is { Length: > 0 } blob
                && !keptBlobs.Contains(blob))
            {
                await DeleteBlobAsync(blob);
            }
        }

        // Delete-all + insert-new (görsel setinin stabil kimliğe ihtiyacı yok — sipariş satırı deseni).
        await _repository.DeleteManyAsync(existing, autoSave: false);
        foreach (var img in normalized)
        {
            var entity = new EntityImage(
                en, entityId, img.SourceType, img.Url, img.BlobName, img.FileName, img.DisplayOrder, img.IsDefault);
            await _repository.InsertAsync(entity, autoSave: false);
        }
    }

    public virtual async Task<string?> GetDefaultPreviewUrlAsync(string entityName, Guid entityId)
    {
        var rows = await LoadOrderedAsync(entityName, entityId);
        var pick = rows.FirstOrDefault(r => r.IsDefault) ?? rows.FirstOrDefault();
        if (pick is null)
        {
            return null;
        }

        return await BuildPreviewAsync(pick.SourceType, pick.Url, pick.BlobName);
    }

    public virtual async Task<Dictionary<Guid, string?>> GetDefaultPreviewMapAsync(
        string entityName, IReadOnlyCollection<Guid> entityIds)
    {
        var result = new Dictionary<Guid, string?>();
        if (entityIds == null || entityIds.Count == 0)
        {
            return result;
        }

        var en = (entityName ?? string.Empty).Trim();
        var ids = entityIds.Distinct().ToList();
        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == en && ids.Contains(x.EntityId))
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id));

        foreach (var group in rows.GroupBy(r => r.EntityId))
        {
            var pick = group.FirstOrDefault(r => r.IsDefault) ?? group.First();
            result[group.Key] = await BuildPreviewAsync(pick.SourceType, pick.Url, pick.BlobName);
        }

        return result;
    }

    public virtual async Task<EntityImageUploadResultDto> UploadAsync(EntityImageUploadDto input)
    {
        var uploaded = await ImageUploadPipeline.UploadAsync(
            _container, GuidGenerator, input.FileName, input.Content, EntityImageConsts.MaxImageSizeBytes, ErrorCodePrefix);

        return new EntityImageUploadResultDto
        {
            BlobName = uploaded.BlobName,
            PreviewDataUrl = uploaded.PreviewDataUrl,
        };
    }

    private async Task<List<EntityImage>> LoadOrderedAsync(string entityName, Guid entityId)
    {
        var en = (entityName ?? string.Empty).Trim();
        return await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == en && x.EntityId == entityId)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id));
    }

    private async Task<string?> BuildPreviewAsync(ProductImageSourceType sourceType, string? url, string? blobName)
    {
        if (sourceType == ProductImageSourceType.Url)
        {
            return url;
        }

        if (sourceType == ProductImageSourceType.Upload && blobName is { Length: > 0 } blob)
        {
            var thumb = await _container.GetAllBytesOrNullAsync(ImageUploadPipeline.ThumbnailNameOf(blob));
            return thumb is null ? null : ImageUploadPipeline.BuildPreviewDataUrl(thumb);
        }

        return null;
    }

    private async Task DeleteBlobAsync(string blobName)
    {
        await _container.DeleteAsync(blobName);
        await _container.DeleteAsync(ImageUploadPipeline.ThumbnailNameOf(blobName));
    }

    // Kaynağı boş olanları ele + aynı URL/dosya iki kez giremez (ilk kalır) + tekil-default (birden fazlaysa ilki;
    // hiç yoksa ilk görsel) + DisplayOrder'a göre sırala.
    private static List<EntityImageEditDto> Normalize(List<EntityImageEditDto>? images)
    {
        var list = (images ?? new List<EntityImageEditDto>())
            .Where(i => i.SourceType == ProductImageSourceType.Url
                ? !string.IsNullOrWhiteSpace(i.Url)
                : !string.IsNullOrWhiteSpace(i.BlobName))
            .OrderBy(i => i.DisplayOrder)
            .ToList();

        var seenUrl = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<EntityImageEditDto>();
        foreach (var i in list)
        {
            if (i.Url is { Length: > 0 } u && !seenUrl.Add(u))
            {
                continue;
            }

            if (i.FileName is { Length: > 0 } f && !seenFile.Add(f))
            {
                continue;
            }

            deduped.Add(i);
        }

        var firstDefaultSeen = false;
        for (var idx = 0; idx < deduped.Count; idx++)
        {
            deduped[idx].DisplayOrder = idx;
            if (deduped[idx].IsDefault)
            {
                if (firstDefaultSeen)
                {
                    deduped[idx].IsDefault = false;
                }

                firstDefaultSeen = true;
            }
        }

        if (!firstDefaultSeen && deduped.Count > 0)
        {
            deduped[0].IsDefault = true;
        }

        return deduped;
    }
}
