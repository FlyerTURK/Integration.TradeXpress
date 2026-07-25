using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace Integration.Framework.Application;

/// <summary>
/// Framework generic CRUD AppService tabanı. ABP <see cref="CrudAppService{TEntity,TGetOutputDto,TGetListOutputDto,TKey,TGetListInput,TCreateInput,TUpdateInput}"/>'i,
/// vendor-agnostik + whitelist'li + aksan-katlayan <see cref="ListQueryableExtensions.ApplyListRequest"/>
/// motoruyla birleştirir. Böylece her entity'de <c>GetListAsync</c> (filtre/sıralama/arama/sayfalama)
/// elle yazılmaz; alt sınıf yalnızca <see cref="AllowedListFields"/>'i (izin verilen alanlar) verir.
///
/// <para>Create/Update/Delete tarafı ABP'nin standart davranışıdır; zengin domain
/// constructor'ları için alt sınıf ilgili metotları override edebilir.</para>
/// </summary>
public abstract class FrameworkCrudAppService<TEntity, TKey, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    : CrudAppService<TEntity, TGetDto, TListDto, TKey, TListRequest, TCreateInput, TUpdateInput>
    where TEntity : class, IEntity<TKey>
    where TGetDto : class
    where TListDto : class
    where TListRequest : ListRequestDto
    where TCreateInput : class
    where TUpdateInput : class
{
    protected FrameworkCrudAppService(IRepository<TEntity, TKey> repository)
        : base(repository)
    {
    }

    /// <summary>
    /// Filtre/sıralama/global aramaya izin verilen alanlar (whitelist). <c>null</c> veya boş
    /// bırakılırsa <typeparamref name="TEntity"/>'nin tüm public property'leri kabul edilir.
    /// Güvenlik için alt sınıfların açıkça vermesi önerilir.
    /// </summary>
    protected virtual ISet<string>? AllowedListFields => null;

    protected override async Task<IQueryable<TEntity>> CreateFilteredQueryAsync(TListRequest input)
    {
        var query = await Repository.GetQueryableAsync();
        // Tek noktadan: kolon filtreleri + global fold araması + sıralama (+ Id tie-breaker)
        // + savunma sınırları (clamp/cap). Sayfalama ABP'nin ApplyPagingAsync'i yapar.
        return query.ApplyListRequest(input, AllowedListFields);
    }

    /// <summary>AllPages (-1) istendiğinde ABP'nin sayfalamasını TAMAMEN atlar — ne Skip ne Take;
    /// aksi halde ABP'nin standart sayfalaması aynen çalışır.</summary>
    protected override IQueryable<TEntity> ApplyPaging(IQueryable<TEntity> query, TListRequest input)
    {
        // <= 0 (== -1 değil): 0 ve negatiflerin hepsi "hepsi" demektir — tek bir değere bakmak,
        // 0 gönderen çağrıyı sessizce tek satıra düşürürdü.
        return input.MaxResultCount <= 0
            ? query
            : base.ApplyPaging(query, input);
    }

    protected override IQueryable<TEntity> ApplySorting(IQueryable<TEntity> query, TListRequest input)
    {
        // Sıralama ApplyListRequest içinde whitelist'le uygulandı; ABP'nin (dynamic-LINQ
        // tabanlı) tekrar sıralamasını devre dışı bırak.
        return query;
    }
}
