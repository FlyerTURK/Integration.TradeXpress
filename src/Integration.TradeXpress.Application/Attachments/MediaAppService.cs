using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Merkezi medya kütüphanesi (DAM) servisi — company-scoped, SELF-CONTAINED. Yükleme/URL-import içeriği bizim blob'umuza
/// yazar (URL saklanmaz); içerik-hash ile dedup eder (aynı içerik reuse). Görselde ImageSharp thumbnail = poster; videoda
/// poster istemci-yakalamayla (SetPoster) sonradan. İçerik/poster BAYTLARI buradan dönmez → Id-scoped stream controller.
/// </summary>
[Authorize]
public class MediaAppService : TradeXpressAppService, IMediaAppService
{
    private const string ErrorCodePrefix = "TradeXpress:Media";

    private readonly IRepository<Media, Guid> _repository;
    private readonly IRepository<MediaFolder, Guid> _folderRepository;
    private readonly IBlobContainer<MediaContainer> _container;
    private readonly ICurrentCompany _currentCompany;
    private readonly IHttpClientFactory _httpClientFactory;

    public MediaAppService(
        IRepository<Media, Guid> repository,
        IRepository<MediaFolder, Guid> folderRepository,
        IBlobContainer<MediaContainer> container,
        ICurrentCompany currentCompany,
        IHttpClientFactory httpClientFactory)
    {
        _repository = repository;
        _folderRepository = folderRepository;
        _container = container;
        _currentCompany = currentCompany;
        _httpClientFactory = httpClientFactory;
    }

    public virtual async Task<MediaDto> UploadAsync(MediaUploadDto input)
    {
        return await StoreAsync(input.FileName, input.Content, input.FolderId);
    }

    public virtual async Task<MediaDto> ImportFromUrlAsync(MediaImportUrlDto input)
    {
        var content = await FetchFromUrlAsync(input.Url);
        var fileName = ResolveImportFileName(input.Url, input.FileName);
        return await StoreAsync(fileName, content, input.FolderId);
    }

    public virtual async Task<PagedResultDto<MediaDto>> GetLibraryAsync(MediaListRequestDto input)
    {
        var query = (await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == _currentCompany.Id);   // company-scope (tenant filtresi ABP'den otomatik)

        if (input.FilterByFolder)
        {
            query = query.Where(x => x.FolderId == input.FolderId);   // seçili klasör (null = klasörsüz/kök)
        }

        if (input.MediaType.HasValue)
        {
            query = query.Where(x => x.MediaType == input.MediaType.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var f = input.Filter.Trim();
            query = query.Where(x => x.FileName.Contains(f));
        }

        var total = await AsyncExecuter.CountAsync(query);
        var take = input.MaxResultCount <= 0 ? 50 : input.MaxResultCount;
        var rows = await AsyncExecuter.ToListAsync(
            query.OrderByDescending(x => x.CreationTime).ThenBy(x => x.Id)
                .Skip(input.SkipCount).Take(take));

        return new PagedResultDto<MediaDto>(total, rows.Select(ToDto).ToList());
    }

    public virtual async Task<List<MediaDto>> GetByIdsAsync(List<Guid> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            return new List<MediaDto>();
        }

        var distinct = ids.Distinct().ToList();
        var rows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x => distinct.Contains(x.Id)));
        return rows.Select(ToDto).ToList();
    }

    public virtual async Task<MediaDto> SetPosterAsync(SetMediaPosterDto input)
    {
        if (input.PosterContent == null || input.PosterContent.Length == 0)
        {
            throw new BusinessException(ErrorCodePrefix + ":PosterEmpty");
        }

        var media = await _repository.GetAsync(input.MediaId);   // tenant/company data-filter

        // Var olan poster blob'unu YENİDEN KULLAN + ÜZERİNE YAZ (delete-eski + create-yeni yerine TEK op). Aksi halde aynı
        // UoW'da iki blob-container op'u AbpBlobContainers.ConcurrencyStamp çakışması veriyordu → re-capture'da "Beklenmeyen
        // hata" (ilk poster'da eski blob yok → tek op → çalışıyordu; ikinci kez → delete+create → patlıyordu). Blob adı sabit
        // kalır; tarayıcı cache'i PosterUrl'deki ?v= (LastModificationTime) cache-buster'ı ile tazelenir.
        var posterBlob = string.IsNullOrEmpty(media.PosterBlobName)
            ? GuidGenerator.Create().ToString("N") + ".jpg"
            : media.PosterBlobName;
        await _container.SaveAsync(posterBlob, input.PosterContent, overrideExisting: true);
        media.SetPoster(posterBlob);
        if (input.Width.HasValue || input.Height.HasValue)
        {
            media.SetDimensions(input.Width, input.Height);
        }

        if (input.DurationSeconds.HasValue)
        {
            media.SetDuration(input.DurationSeconds);
        }

        await _repository.UpdateAsync(media, autoSave: true);
        return ToDto(media);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var media = await _repository.GetAsync(id);
        await _container.DeleteAsync(media.BlobName);
        if (media.PosterBlobName is { Length: > 0 } poster)
        {
            await _container.DeleteAsync(poster);
        }

        await _repository.DeleteAsync(media, autoSave: true);
    }

    // ── Klasörler (organizasyon) ──

    public virtual async Task<List<MediaFolderDto>> GetFoldersAsync()
    {
        var rows = await AsyncExecuter.ToListAsync(
            (await _folderRepository.GetQueryableAsync())
                .Where(x => x.CompanyId == _currentCompany.Id)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name));
        return rows.Select(ToFolderDto).ToList();
    }

    public virtual async Task<MediaFolderDto> CreateFolderAsync(CreateMediaFolderDto input)
    {
        await EnsureParentValidAsync(input.ParentId, null);
        var folder = new MediaFolder(_currentCompany.Id, input.Name, input.ParentId);
        await _folderRepository.InsertAsync(folder, autoSave: true);
        return ToFolderDto(folder);
    }

    public virtual async Task<MediaFolderDto> UpdateFolderAsync(Guid id, UpdateMediaFolderDto input)
    {
        var folder = await _folderRepository.GetAsync(id);
        await EnsureParentValidAsync(input.ParentId, id);
        folder.SetName(input.Name);
        folder.SetParent(input.ParentId);
        await _folderRepository.UpdateAsync(folder, autoSave: true);
        return ToFolderDto(folder);
    }

    public virtual async Task DeleteFolderAsync(Guid id)
    {
        var folder = await _folderRepository.GetAsync(id);

        // İçerik KORUNUR: bu klasördeki medyayı üst klasöre taşı (silme YOK).
        var media = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.FolderId == id));
        foreach (var m in media)
        {
            m.SetFolder(folder.ParentId);
            await _repository.UpdateAsync(m, autoSave: false);
        }

        // Alt klasörleri de üst klasöre taşı (ağaç kopmaz).
        var children = await AsyncExecuter.ToListAsync(
            (await _folderRepository.GetQueryableAsync()).Where(x => x.ParentId == id));
        foreach (var c in children)
        {
            c.SetParent(folder.ParentId);
            await _folderRepository.UpdateAsync(c, autoSave: false);
        }

        await _folderRepository.DeleteAsync(folder, autoSave: true);
    }

    public virtual async Task MoveToFolderAsync(MoveMediaToFolderDto input)
    {
        if (input.MediaIds == null || input.MediaIds.Count == 0)
        {
            return;
        }

        if (input.FolderId.HasValue)
        {
            await EnsureFolderExistsAsync(input.FolderId.Value);
        }

        var ids = input.MediaIds.Distinct().ToList();
        var media = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == _currentCompany.Id && ids.Contains(x.Id)));
        foreach (var m in media)
        {
            m.SetFolder(input.FolderId);
            await _repository.UpdateAsync(m, autoSave: false);
        }
    }

    private async Task<bool> FolderExistsAsync(Guid folderId)
    {
        return await AsyncExecuter.AnyAsync(
            (await _folderRepository.GetQueryableAsync())
                .Where(x => x.Id == folderId && x.CompanyId == _currentCompany.Id));
    }

    private async Task EnsureFolderExistsAsync(Guid folderId)
    {
        if (!await FolderExistsAsync(folderId))
        {
            throw new BusinessException(ErrorCodePrefix + ":FolderNotFound");
        }
    }

    // Yeni parent geçerli mi: var olmalı + kendisi/alt-ağacı olamaz (döngü engeli).
    private async Task EnsureParentValidAsync(Guid? parentId, Guid? selfId)
    {
        if (!parentId.HasValue)
        {
            return;
        }

        if (selfId.HasValue && parentId.Value == selfId.Value)
        {
            throw new BusinessException(ErrorCodePrefix + ":FolderCycle");
        }

        await EnsureFolderExistsAsync(parentId.Value);

        if (!selfId.HasValue)
        {
            return;
        }

        // parent zincirini köke yürü; selfId'ye rastlarsak döngü oluşur.
        var chain = await AsyncExecuter.ToListAsync(
            (await _folderRepository.GetQueryableAsync())
                .Where(x => x.CompanyId == _currentCompany.Id)
                .Select(x => new { x.Id, x.ParentId }));
        var byId = chain.ToDictionary(x => x.Id, x => x.ParentId);
        var cursor = parentId;
        while (cursor.HasValue)
        {
            if (cursor.Value == selfId.Value)
            {
                throw new BusinessException(ErrorCodePrefix + ":FolderCycle");
            }

            cursor = byId.TryGetValue(cursor.Value, out var p) ? p : null;
        }
    }

    // ── StoreAsync: tür algıla → guard → hash-dedup → blob (+ görsel poster) → kayıt ──
    private async Task<MediaDto> StoreAsync(string fileName, byte[] content, Guid? folderId)
    {
        if (content == null || content.Length == 0)
        {
            throw new BusinessException(ErrorCodePrefix + ":Empty");
        }

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var kind = ResolveKind(extension);
        EnsureSize(content.Length, kind);

        var hash = ComputeHash(content);
        var existing = await FindByHashAsync(hash);
        if (existing != null)
        {
            return ToDto(existing);   // dedup → mevcut medya reuse (yeni blob yazılmaz)
        }

        string blobName;
        string? posterBlobName = null;

        if (kind == MediaType.Image)
        {
            // Ortak görsel pipeline: guard + thumbnail + blob (+ ThumbnailNameOf poster). DRY.
            var uploaded = await ImageUploadPipeline.UploadAsync(
                _container, GuidGenerator, fileName!, content, (int)MediaConsts.MaxImageSizeBytes, ErrorCodePrefix);
            blobName = uploaded.BlobName;
            posterBlobName = ImageUploadPipeline.ThumbnailNameOf(blobName);
        }
        else
        {
            // Video: ham blob (poster istemci-yakalamayla sonra ayarlanır).
            blobName = GuidGenerator.Create().ToString("N") + extension;
            await _container.SaveAsync(blobName, content);
        }

        var media = new Media(
            _currentCompany.Id,
            kind,
            blobName,
            SanitizeFileName(fileName),
            ContentTypeOf(extension, kind),
            content.LongLength,
            hash);
        if (posterBlobName != null)
        {
            media.SetPoster(posterBlobName);
        }

        if (folderId.HasValue)
        {
            media.SetFolder(folderId);
        }

        await _repository.InsertAsync(media, autoSave: true);
        return ToDto(media);
    }

    private async Task<Media?> FindByHashAsync(string hash)
    {
        return await AsyncExecuter.FirstOrDefaultAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == _currentCompany.Id && x.ContentHash == hash));
    }

    private async Task<byte[]> FetchFromUrlAsync(string url)
    {
        // SSRF guard (tasarım aşaması): yalnız http/https + boyut/timeout. PRODUCTION'da iç-IP blocklist eklenecek.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessException(ErrorCodePrefix + ":ImportUrlInvalid");
        }

        var client = _httpClientFactory.CreateClient("MediaImport");
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException(ErrorCodePrefix + ":ImportFailed").WithData("Status", (int)response.StatusCode);
        }

        // En yüksek sınır (video); tür guard'ı StoreAsync'te asıl türe göre daraltır.
        if (response.Content.Headers.ContentLength is { } declared && declared > MediaConsts.MaxVideoSizeBytes)
        {
            throw new BusinessException(ErrorCodePrefix + ":ImportTooLarge");
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.LongLength > MediaConsts.MaxVideoSizeBytes)
        {
            throw new BusinessException(ErrorCodePrefix + ":ImportTooLarge");
        }

        return bytes;
    }

    private static string ResolveImportFileName(string url, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided.Trim();
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return "media";
    }

    private static MediaType ResolveKind(string extension)
    {
        if (ImageTypes.ContainsKey(extension))
        {
            return MediaType.Image;
        }

        if (VideoTypes.ContainsKey(extension))
        {
            return MediaType.Video;
        }

        throw new BusinessException(ErrorCodePrefix + ":TypeNotSupported");
    }

    private static void EnsureSize(long size, MediaType kind)
    {
        var max = kind == MediaType.Video ? MediaConsts.MaxVideoSizeBytes : MediaConsts.MaxImageSizeBytes;
        if (size > max)
        {
            throw new BusinessException(ErrorCodePrefix + ":TooLarge").WithData("MaxMb", max / (1024 * 1024));
        }
    }

    private static string ContentTypeOf(string extension, MediaType kind)
    {
        if (kind == MediaType.Image && ImageTypes.TryGetValue(extension, out var it))
        {
            return it;
        }

        if (kind == MediaType.Video && VideoTypes.TryGetValue(extension, out var vt))
        {
            return vt;
        }

        return "application/octet-stream";
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty).Trim();
        return string.IsNullOrEmpty(name) ? "media" : name;
    }

    private static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    // Mapperly (scalar alanlar) + hesaplanan URL enrich'i. INSTANCE (ObjectMapper) — "statik entity→DTO mapper YASAK"
    // konvansiyonuna uyar; poster/içerik URL'i + HasPoster mapper'da ignore, burada Id-scoped endpoint + cache-buster kurulur.
    private MediaDto ToDto(Media m)
    {
        var dto = ObjectMapper.Map<Media, MediaDto>(m);
        dto.HasPoster = !string.IsNullOrEmpty(m.PosterBlobName);
        dto.PosterUrl = string.IsNullOrEmpty(m.PosterBlobName)
            ? null
            : $"/api/media/{m.Id}/poster?v={(m.LastModificationTime ?? m.CreationTime).Ticks}";
        dto.ContentUrl = $"/api/media/{m.Id}/content";
        return dto;
    }

    private MediaFolderDto ToFolderDto(MediaFolder f)
    {
        return ObjectMapper.Map<MediaFolder, MediaFolderDto>(f);
    }

    // İzinli görsel türleri (uzantı → mime).
    private static readonly Dictionary<string, string> ImageTypes = new()
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    // İzinli video türleri (uzantı → mime; stream controller ResolveContentType ile hizalı).
    private static readonly Dictionary<string, string> VideoTypes = new()
    {
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".ogg"] = "video/ogg",
        [".ogv"] = "video/ogg",
        [".mov"] = "video/quicktime",
        [".m4v"] = "video/x-m4v",
    };
}
