using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Entity→medya LİNK seti servisi — bir kaydın (EntityName+EntityId) kütüphane medyalarına referanslarını okur/replace eder.
/// Medya İÇERİĞİ silinmez (kütüphanede kalır); yalnız link'ler (sıra/varsayılan/aktif) yönetilir. Parent AppService/panel
/// kayıt sonrası <see cref="ReplaceForAsync"/> ile link setini değiştirir (graph-save deseni; EntityImage ile aynı).
/// </summary>
public interface IEntityMediaAppService : IApplicationService
{
    Task<List<EntityMediaLinkEditDto>> GetForAsync(string entityName, Guid entityId);

    Task ReplaceForAsync(string entityName, Guid entityId, Guid? companyId, List<EntityMediaLinkEditDto> links);

    /// <summary>Verilen sahip kayıtların (EntityName + ownerIds) VARSAYILAN medyasının poster URL'i — liste grid önizlemesi (tek batch; N+1 yok).</summary>
    Task<Dictionary<Guid, string?>> GetDefaultPosterMapAsync(string entityName, IReadOnlyCollection<Guid> ownerIds);
}
