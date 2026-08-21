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
            .Select(n => new EntityNote(en, entityId, n.Title, n.Text!, n.DisplayOrder))
            .ToList();
        await _repository.InsertManyAsync(replacements, autoSave: true);
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
