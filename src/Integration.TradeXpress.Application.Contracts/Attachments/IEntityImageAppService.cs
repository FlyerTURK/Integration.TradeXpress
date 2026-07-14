using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik görsel servisi — herhangi bir entity kaydına (EntityName + EntityId) görsel eklemek/okumak için.
/// Parent AppService kayıt sonrası <see cref="ReplaceForAsync"/> ile o kaydın görsel setini değiştirir (graph-save);
/// UI <see cref="UploadAsync"/> ile blob yükler, <see cref="GetForAsync"/> ile mevcut görselleri okur.
/// </summary>
public interface IEntityImageAppService : IApplicationService
{
    /// <summary>Bir kaydın (EntityName, EntityId) görselleri — DisplayOrder'a göre sıralı, önizleme dolu.</summary>
    Task<List<EntityImageDto>> GetForAsync(string entityName, Guid entityId);

    /// <summary>Bir kaydın görsel setini TÜMÜYLE değiştirir (delete-all + insert-new; kaldırılan Upload blob'ları
    /// + thumbnail'leri temizlenir). Normalize: dedup (URL/dosya) + tekil-default + sıra.</summary>
    Task ReplaceForAsync(string entityName, Guid entityId, List<EntityImageEditDto> images);

    /// <summary>Bir kaydın VARSAYILAN görselinin önizleme URL'i (liste grid thumbnail'i) — yoksa null.</summary>
    Task<string?> GetDefaultPreviewUrlAsync(string entityName, Guid entityId);

    /// <summary>Çok kaydın varsayılan görsel önizlemesi TEK sorguda (liste grid'i N+1 olmadan) — kaydı görsel olmayanlar
    /// sözlükte yer almaz. Anahtar EntityId, değer önizleme URL'i.</summary>
    Task<Dictionary<Guid, string?>> GetDefaultPreviewMapAsync(string entityName, IReadOnlyCollection<Guid> entityIds);

    /// <summary>Görsel dosyası yükler (guard + thumbnail + blob) → blob adı + önizleme. Henüz bir kayda bağlamaz.</summary>
    Task<EntityImageUploadResultDto> UploadAsync(EntityImageUploadDto input);
}
