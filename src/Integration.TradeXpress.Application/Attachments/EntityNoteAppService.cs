using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity-agnostik not servisi — herhangi bir kaydın (EntityName + EntityId) not setini yönetir. Blob yok, en sade
/// attachment ailesi. Kaydetme: parent AppService kayıt sonrası <see cref="ReplaceForAsync"/> ile bu tabloyu değiştirir
/// (graph-save). SUNUCU-İÇİ (<c>RemoteService(false)</c>): entityName/entityId keyfi client'tan gelmemeli — sahip
/// AppService güvenlik sınırını tutar.
/// </summary>
[Authorize]
[RemoteService(false)]
public class EntityNoteAppService : TradeXpressAppService, IEntityNoteAppService
{
    private readonly IRepository<EntityNote, Guid> _repository;

    public EntityNoteAppService(IRepository<EntityNote, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<List<EntityNoteDto>> GetForAsync(string entityName, Guid entityId)
    {
        var rows = await LoadOrderedAsync(entityName, entityId);
        return rows.Select(row => new EntityNoteDto
        {
            Id = row.Id,
            Title = row.Title,
            Text = row.Text,
            DisplayOrder = row.DisplayOrder,
            CreationTime = row.CreationTime,
        }).ToList();
    }

    public virtual async Task ReplaceForAsync(string entityName, Guid entityId, List<EntityNoteEditDto> notes)
    {
        var en = (entityName ?? string.Empty).Trim();
        var normalized = Normalize(notes);
        var existing = await LoadOrderedAsync(en, entityId);

        // Delete-all + insert-new (not setinin stabil kimliğe ihtiyacı yok — sipariş satırı deseni).
        await _repository.DeleteManyAsync(existing, autoSave: false);
        foreach (var note in normalized)
        {
            var entity = new EntityNote(en, entityId, note.Title, note.Text!, note.DisplayOrder);
            await _repository.InsertAsync(entity, autoSave: false);
        }
    }

    private async Task<List<EntityNote>> LoadOrderedAsync(string entityName, Guid entityId)
    {
        var en = (entityName ?? string.Empty).Trim();
        return await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == en && x.EntityId == entityId)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.CreationTime));
    }

    // Metni boş olan satırları ele + DisplayOrder'a göre sırala/yeniden numaralandır.
    private static List<EntityNoteEditDto> Normalize(List<EntityNoteEditDto>? notes)
    {
        var list = (notes ?? new List<EntityNoteEditDto>())
            .Where(n => !string.IsNullOrWhiteSpace(n.Text))
            .OrderBy(n => n.DisplayOrder)
            .ToList();

        for (var idx = 0; idx < list.Count; idx++)
        {
            list[idx].DisplayOrder = idx;
        }

        return list;
    }
}
