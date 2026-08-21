using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity→medya LİNK seti servisi — bir kaydın (EntityName+EntityId) kütüphane medyalarına referanslarını okur/replace eder.
/// Medya İÇERİĞİ silinmez (kütüphanede kalır); yalnız link meta'sı (sıra/varsayılan/aktif) yönetilir. Replace-all deseni
/// (EntityImage ile aynı): tekil-default + varsayılan-pasif-olamaz normalize edilir.
/// </summary>
[Authorize]
public class EntityMediaAppService : TradeXpressAppService, IEntityMediaAppService
{
    private readonly IRepository<EntityMediaLink, Guid> _repository;
    private readonly IMediaAppService _media;

    public EntityMediaAppService(IRepository<EntityMediaLink, Guid> repository, IMediaAppService media)
    {
        _repository = repository;
        _media = media;
    }

    public virtual async Task<List<EntityMediaLinkEditDto>> GetForAsync(string entityName, Guid entityId)
    {
        var en = (entityName ?? string.Empty).Trim();
        var links = await LoadOrderedAsync(en, entityId);
        if (links.Count == 0)
        {
            return new List<EntityMediaLinkEditDto>();
        }

        var mediaById = (await _media.GetByIdsAsync(links.Select(l => l.MediaId).Distinct().ToList()))
            .ToDictionary(m => m.Id);

        return links
            .Where(l => mediaById.ContainsKey(l.MediaId))   // yetim link (medya kütüphaneden silinmiş) → atla
            .Select(l => new EntityMediaLinkEditDto
            {
                MediaId = l.MediaId,
                DisplayOrder = l.DisplayOrder,
                IsDefault = l.IsDefault,
                IsActive = l.IsActive,
                Media = mediaById[l.MediaId],
            })
            .ToList();
    }

    public virtual async Task<List<PushMediaDto>> GetPushMediaAsync(string entityName, Guid entityId, MediaType? mediaType = null)
    {
        var en = (entityName ?? string.Empty).Trim();
        var links = (await LoadOrderedAsync(en, entityId)).Where(l => l.IsActive).ToList();
        if (links.Count == 0)
        {
            return new List<PushMediaDto>();
        }

        // Tür, link'te DEĞİL medyada durur → süzmek için medyaları çözmek zorunlu (tek batch).
        var mediaById = (await _media.GetByIdsAsync(links.Select(l => l.MediaId).Distinct().ToList()))
            .ToDictionary(m => m.Id);

        return links
            .Where(l => mediaById.ContainsKey(l.MediaId))   // yetim link (medya kütüphaneden silinmiş) → atla
            .Where(l => mediaType is null || mediaById[l.MediaId].MediaType == mediaType)
            .OrderByDescending(l => l.IsDefault)            // cover HER ZAMAN ilk — DisplayOrder'ı büyük olsa bile
            .ThenBy(l => l.DisplayOrder)
            .ThenBy(l => l.Id)                             // eşit sırada kararlı düzen (push çıktısı tekrarlanabilir olsun)
            .Select(l => new PushMediaDto
            {
                MediaId = l.MediaId,
                MediaType = mediaById[l.MediaId].MediaType,
                IsDefault = l.IsDefault,
                DisplayOrder = l.DisplayOrder,
            })
            .ToList();
    }

    public virtual async Task ReplaceForAsync(string entityName, Guid entityId, Guid? companyId, List<EntityMediaLinkEditDto> links)
    {
        var en = (entityName ?? string.Empty).Trim();
        var normalized = Normalize(links);
        var existing = await LoadOrderedAsync(en, entityId);

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
            .Select(l => new EntityMediaLink(companyId, en, entityId, l.MediaId, l.DisplayOrder, l.IsDefault, l.IsActive))
            .ToList();
        await _repository.InsertManyAsync(replacements, autoSave: true);
    }

    public virtual async Task<Dictionary<Guid, string?>> GetDefaultPosterMapAsync(string entityName, IReadOnlyCollection<Guid> ownerIds)
    {
        var result = new Dictionary<Guid, string?>();
        if (ownerIds == null || ownerIds.Count == 0)
        {
            return result;
        }

        var en = (entityName ?? string.Empty).Trim();
        var ids = ownerIds.Distinct().ToList();
        var links = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == en && ids.Contains(x.EntityId))
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id));

        // Sahip başına VARSAYILAN (yoksa ilk) link → medya.
        var pickByOwner = new Dictionary<Guid, Guid>();
        foreach (var group in links.GroupBy(l => l.EntityId))
        {
            var pick = group.FirstOrDefault(l => l.IsDefault) ?? group.First();
            pickByOwner[group.Key] = pick.MediaId;
        }

        var mediaById = (await _media.GetByIdsAsync(pickByOwner.Values.Distinct().ToList()))
            .ToDictionary(m => m.Id);
        foreach (var kv in pickByOwner)
        {
            result[kv.Key] = mediaById.TryGetValue(kv.Value, out var m) ? m.PosterUrl : null;
        }

        return result;
    }

    private async Task<List<EntityMediaLink>> LoadOrderedAsync(string entityName, Guid entityId)
    {
        return await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.EntityName == entityName && x.EntityId == entityId)
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id));
    }

    // MediaId'ye göre dedup + sıra yeniden + tekil-default + varsayılan-pasif-olamaz.
    private static List<EntityMediaLinkEditDto> Normalize(List<EntityMediaLinkEditDto>? links)
    {
        var ordered = (links ?? new List<EntityMediaLinkEditDto>())
            .Where(l => l.MediaId != Guid.Empty)
            .OrderBy(l => l.DisplayOrder)
            .ToList();

        var seen = new HashSet<Guid>();
        var deduped = new List<EntityMediaLinkEditDto>();
        foreach (var l in ordered)
        {
            if (seen.Add(l.MediaId))
            {
                deduped.Add(l);
            }
        }

        var firstDefaultSeen = false;
        for (var i = 0; i < deduped.Count; i++)
        {
            deduped[i].DisplayOrder = i;
            if (deduped[i].IsDefault)
            {
                if (firstDefaultSeen)
                {
                    deduped[i].IsDefault = false;
                }
                else
                {
                    deduped[i].IsActive = true;   // VARSAYILAN medya pasif OLAMAZ
                    firstDefaultSeen = true;
                }
            }
        }

        if (!firstDefaultSeen && deduped.Count > 0)
        {
            deduped[0].IsDefault = true;
            deduped[0].IsActive = true;
        }

        return deduped;
    }
}
