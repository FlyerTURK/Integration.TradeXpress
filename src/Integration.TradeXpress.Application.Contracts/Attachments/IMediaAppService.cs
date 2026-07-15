using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Merkezi medya kütüphanesi (DAM) servisi — company-scoped. Yükleme/URL-import içeriği SELF-CONTAINED blob'a yazar
/// + içerik-hash ile dedup eder (aynı içerik reuse); kütüphane listeler; video poster'ı istemci-yakalamayla ayarlanır;
/// silme. Ham içerik/poster BAYTLARI bu servisten dönmez — ayrı Id-scoped stream controller'dan akıtılır (BOLA daraltma).
/// </summary>
public interface IMediaAppService : IApplicationService
{
    /// <summary>Dosya yükle → blob + (görselse) thumbnail poster; dedup'lı. Yeni ya da mevcut (reuse) medya döner.</summary>
    Task<MediaDto> UploadAsync(MediaUploadDto input);

    /// <summary>URL'den içe aktar → sunucu fetch (SSRF-guard) → blob'a yaz (URL saklanmaz). Yeni ya da mevcut medya döner.</summary>
    Task<MediaDto> ImportFromUrlAsync(MediaImportUrlDto input);

    /// <summary>Company-scoped kütüphane — arama/tür filtresi + paging.</summary>
    Task<PagedResultDto<MediaDto>> GetLibraryAsync(MediaListRequestDto input);

    /// <summary>Verilen Id'ler için medya (entity link'lerini gösterime çözme) — company/tenant-scoped.</summary>
    Task<List<MediaDto>> GetByIdsAsync(List<Guid> ids);

    /// <summary>Video poster'ını istemci-yakalanan kareyle (JPEG) ayarla (+ opsiyonel süre/boyut).</summary>
    Task<MediaDto> SetPosterAsync(SetMediaPosterDto input);

    /// <summary>Kütüphaneden medya sil (blob + poster + kayıt). Link'ler ayrı yönetilir (yetim link temizliği çağırana).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Company klasör ağacı (hiyerarşik, sıralı).</summary>
    Task<List<MediaFolderDto>> GetFoldersAsync();

    /// <summary>Yeni klasör oluştur.</summary>
    Task<MediaFolderDto> CreateFolderAsync(CreateMediaFolderDto input);

    /// <summary>Klasörü güncelle (yeniden adlandır / taşı). Kendine/alt-ağacına taşıma engellenir.</summary>
    Task<MediaFolderDto> UpdateFolderAsync(Guid id, UpdateMediaFolderDto input);

    /// <summary>Klasörü sil — İÇİNDEKİ MEDYA SİLİNMEZ (üst klasöre taşınır); alt klasörler de üste taşınır.</summary>
    Task DeleteFolderAsync(Guid id);

    /// <summary>Medyayı klasöre taşı (organizasyon).</summary>
    Task MoveToFolderAsync(MoveMediaToFolderDto input);
}
