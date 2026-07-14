using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik not servisi — herhangi bir entity kaydına (EntityName + EntityId) sade metin not eklemek/okumak
/// için. Parent AppService kayıt sonrası <see cref="ReplaceForAsync"/> ile o kaydın not setini değiştirir (graph-save).
/// Blob yok — en sade attachment ailesi. SUNUCU-İÇİ (sahip AppService delege eder).
/// </summary>
public interface IEntityNoteAppService : IApplicationService
{
    /// <summary>Bir kaydın (EntityName, EntityId) notları — DisplayOrder'a göre sıralı.</summary>
    Task<List<EntityNoteDto>> GetForAsync(string entityName, Guid entityId);

    /// <summary>Bir kaydın not setini TÜMÜYLE değiştirir (delete-all + insert-new).</summary>
    Task ReplaceForAsync(string entityName, Guid entityId, List<EntityNoteEditDto> notes);
}
