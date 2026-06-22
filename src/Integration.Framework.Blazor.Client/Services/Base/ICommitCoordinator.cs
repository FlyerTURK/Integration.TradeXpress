using System.Threading.Tasks;
using Integration.Framework.Base.Dtos.Interfaces;
using Volo.Abp.Application.Dtos;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// CRUD orkestratörü — agnostic <c>EditForm</c>'un host'u (liste sayfası / EditHost / DrillList) bunu kullanır.
/// <b>TEK API/persistence sınırı:</b> liste-fetch + commit (Create/Update map'i) + yeni-model fabrikası + sil +
/// navigasyon hepsi burada. Form bu arayüzü BİLMEZ; host'a event (OnCommit/OnGoPrev/Next) fırlatır, host
/// koordinatöre delege eder ("listeleme formu pası StateService'e gönderir").
///
/// İki uygulama, aynı kontrat:
/// <list type="bullet">
/// <item><b>StateService</b> — persistent (üst liste; AppService'e gider). Scoped.</item>
/// <item><b>GraphService</b> — dual-mode (persistent / in-memory). Drill ile per-instance.</item>
/// </list>
/// Create/Update DTO tipleri burada GÖRÜNMEZ — onlar persistent uygulamanın iç mapping detayıdır;
/// arayüz yalnız GetDto (düzenlenen model) + ListDto (liste) + ListRequest (sorgu) üzerinden konuşur.
/// </summary>
public interface ICommitCoordinator<TGetDto, TListDto, TKey, TListRequestDto>
    where TGetDto : class, IGetDto<TKey>, new()
    where TListDto : class, IListDto<TKey>, new()
    where TListRequestDto : class, new()
{
    /// <summary>Yeni-kayıt modeli (bağlam default'larıyla: persistent → new()+context; in-memory → graf düğümü).</summary>
    TGetDto NewModel();

    /// <summary>Düzenlemek için bir kaydı getirir (persistent → AppService.GetAsync; in-memory → listeden).</summary>
    Task<TGetDto> GetForEditAsync(TKey id);

    /// <summary>
    /// Commit: <paramref name="model"/>.Id boş → Create, dolu → Update. Persistent uygulama GetDto'yu
    /// Create/Update'e map'ler + AppService'e yazar + kendi params'ıyla listeyi yeniden çeker; in-memory
    /// uygulama koleksiyona ekler/değiştirir. Dönen: sunucunun/grafın verdiği TAZE GetDto.
    /// </summary>
    Task<TGetDto> CommitAsync(TGetDto model);

    /// <summary>Kaydı siler (persistent → AppService.DeleteAsync; in-memory → koleksiyondan).</summary>
    Task DeleteAsync(TKey id);

    /// <summary>Liste fetch'i — grid buna bağlanır (persistent → server sayfa; in-memory → koleksiyon).
    /// Sonrası TotalCount/konum güncellenir; nav buradan beslenir.</summary>
    Task<PagedResultDto<TListDto>> FetchAsync(TListRequestDto request);

    // ── Navigasyon (host'un toolbar'ı buraya delege eder; hedef GetDto döner, sınırdaysa null) ──
    bool CanGoPrevious { get; }
    bool CanGoNext { get; }
    Task<TGetDto?> GoPreviousAsync();
    Task<TGetDto?> GoNextAsync();
}
