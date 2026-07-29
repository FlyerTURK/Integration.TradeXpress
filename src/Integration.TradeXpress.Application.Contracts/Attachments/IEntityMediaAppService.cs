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

    /// <summary>Pazaryerine GİDECEK medya seti — <see cref="GetForAsync"/>'ten üç farkı vardır ve üçü de push için zorunludur:
    /// <list type="number">
    /// <item>PASİF link'ler elenir (düzenleme yüzeyi onları gösterir; pazaryeri görmemeli).</item>
    /// <item><paramref name="mediaType"/> ile tür süzülür — video isteyen kanal ayrı çağırır; görsel listesine mp4 sızmaz.</item>
    /// <item>Sıra KAPAK-ÖNCE çözülür (<c>IsDefault</c> → <c>DisplayOrder</c>) — DAM'da kapak, sırası kaçıncı olursa olsun kapaktır.</item>
    /// </list></summary>
    Task<List<PushMediaDto>> GetPushMediaAsync(string entityName, Guid entityId, MediaType? mediaType = null);
}
