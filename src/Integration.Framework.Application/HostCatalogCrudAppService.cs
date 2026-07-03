using System.Linq.Expressions;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Base.Querying;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.Framework.Application;

/// <summary>
/// Host-katalog CRUD tabanı: host kaydı (TenantId=null) herkese görünür + tenant kendi kayıtlarını
/// görür (multi-tenant filter disable + <c>TenantId == null || == own</c>). Tenant, global (host)
/// kaydı düzenleyemez/silemez — <see cref="EnsureEditable"/> guard'ı türevin verdiği error-code'la
/// <see cref="BusinessException"/> fırlatır. DTO <see cref="IHostScoped"/> ise <c>IsGlobal</c> map
/// sonrası otomatik doldurulur; ek zenginleştirme için <see cref="EnrichListAsync"/> hook'u var.
///
/// <para>Create/Update mapping'i abstract'tır: entity'ler zengin ctor + SetX kullanır, ObjectMapper
/// ile input→entity map YAPILMAZ; türev <see cref="MapToEntityAsync(TCreateInput)"/> içinde
/// <c>new TEntity(...)</c> kurar.</para>
/// </summary>
public abstract class HostCatalogCrudAppService<TEntity, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    : FrameworkCrudAppService<TEntity, Guid, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    where TEntity : class, IEntity<Guid>, IMultiTenant
    where TGetDto : class
    where TListDto : class
    where TListRequest : ListRequestDto
    where TCreateInput : class
    where TUpdateInput : class
{
    protected HostCatalogCrudAppService(IRepository<TEntity, Guid> repository)
        : base(repository)
    {
        // Multi-tenant filter scope'ları ABP ApplicationService'in hazır DataFilter property'siyle açılır.
    }

    /// <summary>Tenant'ın global (host) kaydı DÜZENLEME denemesinde fırlatılacak error-code (lokalize anahtar).</summary>
    protected abstract string EditGlobalErrorCode { get; }

    /// <summary>Tenant'ın global (host) kaydı SİLME denemesinde fırlatılacak error-code (lokalize anahtar).</summary>
    protected abstract string DeleteGlobalErrorCode { get; }

    /// <summary>Picker listesinin sıralama anahtarı (tipik olarak <c>x =&gt; x.Code</c>).</summary>
    protected abstract Expression<Func<TEntity, string>> PickerOrderSelector { get; }

    public override async Task<PagedResultDto<TListDto>> GetListAsync(TListRequest input)
    {
        await CheckGetListPolicyAsync();

        // Filter-disable scope'u sorgular materialize olana dek açık kalmalı; bu yüzden ABP'nin
        // parçalı hook'ları yerine metot komple override edilir.
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = (await Repository.GetQueryableAsync())
                .Where(BuildVisibilityPredicate())
                .ApplyListRequest(input, AllowedListFields);

            query = ApplyFallbackSort(query, input);

            var totalCount = await AsyncExecuter.CountAsync(query);
            var entities = await AsyncExecuter.ToListAsync(
                query.Skip(input.SkipCount).Take(input.MaxResultCount));

            var dtos = entities.Select(MapListWithIsGlobal).ToList();
            await EnrichListAsync(entities, dtos);

            return new PagedResultDto<TListDto>(totalCount, dtos);
        }
    }

    /// <summary>
    /// Süreç paneli combo'ları için görünür kayıtlar (sıralı, pasifler dahil). Public API imzası
    /// türevin sözleşmesine ait (parametreli/parametresiz varyantlar var) — türev kendi
    /// <c>GetPickerListAsync</c>'ini tek satırla buraya delege eder; böylece conventional-controller
    /// rota çakışması (overload) oluşmaz.
    /// </summary>
    protected virtual async Task<List<TListDto>> GetPickerListCoreAsync(
        Expression<Func<TEntity, bool>>? extraFilter = null)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var query = (await Repository.GetQueryableAsync())
                .Where(BuildVisibilityPredicate());

            if (extraFilter != null)
            {
                query = query.Where(extraFilter);
            }

            var entities = await AsyncExecuter.ToListAsync(query.OrderBy(PickerOrderSelector));

            var dtos = entities.Select(MapListWithIsGlobal).ToList();
            await EnrichListAsync(entities, dtos);

            return dtos;
        }
    }

    public override async Task<TGetDto> UpdateAsync(Guid id, TUpdateInput input)
    {
        await CheckUpdatePolicyAsync();

        var entity = await GetEntityByIdAsync(id);
        EnsureEditable(entity, isDelete: false);

        await MapToEntityAsync(input, entity);
        await Repository.UpdateAsync(entity, autoSave: true);

        return await MapToGetOutputDtoAsync(entity);
    }

    public override async Task DeleteAsync(Guid id)
    {
        await CheckDeletePolicyAsync();

        var entity = await GetEntityByIdAsync(id);
        EnsureEditable(entity, isDelete: true);

        await Repository.DeleteAsync(entity, autoSave: true);
    }

    /// <summary>Create input → yeni entity. Türev zengin ctor + SetX ile kurar (ObjectMapper YOK).</summary>
    protected abstract override Task<TEntity> MapToEntityAsync(TCreateInput createInput);

    /// <summary>Update input → mevcut entity'ye uygula. Türev SetX metotlarıyla yazar (ObjectMapper YOK).</summary>
    protected abstract override Task MapToEntityAsync(TUpdateInput updateInput, TEntity entity);

    /// <summary>
    /// Get/Update/Delete tekil erişimi: host‖own scope içinde arar, bulunamazsa
    /// <see cref="EntityNotFoundException"/>. ABP'nin Get/GetAsync akışları da bunu kullanır.
    /// </summary>
    protected override async Task<TEntity> GetEntityByIdAsync(Guid id)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await Repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));

            if (entity == null)
            {
                throw new EntityNotFoundException(typeof(TEntity), id);
            }

            return entity;
        }
    }

    protected override async Task<TGetDto> MapToGetOutputDtoAsync(TEntity entity)
    {
        var dto = await base.MapToGetOutputDtoAsync(entity);
        SetIsGlobal(dto, entity);
        await EnrichGetAsync(entity, dto);
        return dto;
    }

    /// <summary>Liste/picker DTO'larına ek zenginleştirme (ör. FollowingUnitCode). Varsayılan no-op.</summary>
    protected virtual Task EnrichListAsync(List<TEntity> entities, List<TListDto> dtos)
    {
        return Task.CompletedTask;
    }

    /// <summary>Get DTO'suna ek zenginleştirme. Varsayılan no-op.</summary>
    protected virtual Task EnrichGetAsync(TEntity entity, TGetDto dto)
    {
        return Task.CompletedTask;
    }

    /// <summary>Tenant, global (host) kaydı düzenleyemez/silemez.</summary>
    protected virtual void EnsureEditable(TEntity entity, bool isDelete)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete ? DeleteGlobalErrorCode : EditGlobalErrorCode);
        }
    }

    /// <summary>
    /// Liste/picker/tekil erişimin görünürlük predicate'i. Varsayılan host‖own
    /// (<c>TenantId == null || == own</c>); company-scoped kataloglar override eder.
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>> BuildVisibilityPredicate()
    {
        var tenantId = CurrentTenant.Id;
        return x => x.TenantId == null || x.TenantId == tenantId;
    }

    /// <summary>
    /// Kullanıcı açık sıralama vermediğinde uygulanacak varsayılan sıralama (ör. Code artan).
    /// Varsayılan no-op — ApplyListRequest'in Id tie-breaker'ı geçerli kalır.
    /// </summary>
    protected virtual IQueryable<TEntity> ApplyFallbackSort(IQueryable<TEntity> query, TListRequest input)
    {
        return query;
    }

    /// <summary>Request'te açık sıralama (kolon sort ya da Sorting string) var mı?</summary>
    protected static bool HasExplicitSort(TListRequest input)
    {
        return (input.Sorts is { Count: > 0 }) || !string.IsNullOrWhiteSpace(input.Sorting);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>List DTO map + IsGlobal enrichment — türevlerin özel liste override'ları da kullanır.</summary>
    protected TListDto MapListWithIsGlobal(TEntity entity)
    {
        var dto = MapToGetListOutputDto(entity);
        SetIsGlobal(dto, entity);
        return dto;
    }

    /// <summary>DTO <see cref="IHostScoped"/> ise IsGlobal'i entity'den doldurur.</summary>
    protected static void SetIsGlobal(object dto, TEntity entity)
    {
        if (dto is IHostScoped hostScoped)
        {
            hostScoped.IsGlobal = entity.TenantId == null;
        }
    }
}
