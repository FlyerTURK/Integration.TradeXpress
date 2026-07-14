using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik doküman servisi — herhangi bir entity kaydına (EntityName + EntityId) blob dosya eki eklemek/okumak
/// için. Parent AppService kayıt sonrası <see cref="ReplaceForAsync"/> ile o kaydın doküman setini değiştirir
/// (graph-save); UI <see cref="UploadAsync"/> ile blob yükler, <see cref="GetForAsync"/> ile mevcutları okur,
/// <see cref="DownloadAsync"/> ile içeriği geri çeker.
/// </summary>
public interface IEntityDocumentAppService : IApplicationService
{
    /// <summary>Bir kaydın (EntityName, EntityId) dokümanları — DisplayOrder'a göre sıralı.</summary>
    Task<List<EntityDocumentDto>> GetForAsync(string entityName, Guid entityId);

    /// <summary>Bir kaydın doküman setini TÜMÜYLE değiştirir (delete-all + insert-new; kaldırılan blob'lar temizlenir).</summary>
    Task ReplaceForAsync(string entityName, Guid entityId, List<EntityDocumentEditDto> documents);

    /// <summary>Dosya yükler (guard + blob) → blob adı + MIME + boyut. Henüz bir kayda bağlamaz.</summary>
    Task<EntityDocumentUploadResultDto> UploadAsync(EntityDocumentUploadDto input);

    /// <summary>Kaydedilmiş dokümanın içeriğini (blob) orijinal ad + MIME ile döner — tarayıcı indirmesi için.</summary>
    Task<EntityDocumentDownloadDto> DownloadAsync(Guid id);
}
