using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Commodities;

/// <summary>
/// FollowingUnit (takip edilen para birimi) taşıyan katalog CRUD ara tabanı — Metal/Scrap/Future ortak
/// iskeleti tek yerde: liste (kolon sıralaması yoksa Code artan; katalog küçük → materialize + in-memory),
/// picker (birim düzeni: global önce → AlwaysShowInBalance desc → DisplayOrder asc → birim Code asc →
/// faktör desc → Code asc) ve FollowingUnitCode zenginleştirmesi.
/// </summary>
public abstract class FollowingUnitCatalogAppService<TEntity, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    : HostCatalogCrudAppService<TEntity, TGetDto, TListDto, TListRequest, TCreateInput, TUpdateInput>
    where TEntity : class, IEntity<Guid>, IMultiTenant
    where TGetDto : class, IFollowingUnitDto
    where TListDto : class, IFollowingUnitDto
    where TListRequest : ListRequestDto
    where TCreateInput : class
    where TUpdateInput : class
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (kod/sıra zenginleştirme)

    protected FollowingUnitCatalogAppService(
        IRepository<TEntity, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository)
    {
        _unitRepository = unitRepository;
        LocalizationResource = typeof(TradeXpressResource);
    }

    /// <summary>Entity'nin takip ettiği birim (composite sıralama + kod zenginleştirme anahtarı).</summary>
    protected abstract Guid FollowingUnitIdOf(TEntity entity);

    /// <summary>Picker composite sıralamasında birim içi ikincil anahtar (Factor/FollowingFactor, desc).</summary>
    protected abstract decimal CompositeFactorOf(TEntity entity);

    /// <summary>Entity kodu (in-memory sıralama için).</summary>
    protected abstract string CodeOf(TEntity entity);

    public override async Task<PagedResultDto<TListDto>> GetListAsync(TListRequest input)
    {
        await CheckGetListPolicyAsync();

        using (DataFilter.Disable<IMultiTenant>())
        {
            var filtered = (await Repository.GetQueryableAsync())
                .Where(BuildVisibilityPredicate())
                .ApplyListRequest(input, AllowedListFields);

            // Katalog küçük → filtrelenmiş kümeyi materyalize edip in-memory sırala/sayfala.
            var all = await AsyncExecuter.ToListAsync(filtered);
            var totalCount = all.Count;

            var orders = await GetUnitOrdersAsync(all.Select(FollowingUnitIdOf));

            // Grid listesi: kolon sıralaması yoksa düz Code artan (combo composite sırayı GetPickerList tutar).
            var ordered = HasExplicitSort(input)
                ? all
                : all.OrderBy(CodeOf, StringComparer.OrdinalIgnoreCase).ToList();

            var pageEntities = ordered.Skip(input.SkipCount).Take(input.MaxResultCount).ToList();
            var dtos = pageEntities.Select(MapListWithIsGlobal).ToList();
            ApplyUnitCodes(pageEntities, dtos, orders);
            await EnrichListAsync(pageEntities, dtos);

            return new PagedResultDto<TListDto>(totalCount, dtos);
        }
    }

    /// <summary>Süreç paneli combo'su — liste API'sinin default sırasıyla AYNI (Code asc; 2026-07-05 ürün
    /// kararı: combo, listeleme formunun özel sıralamasız gönderdiği sırayı izler), pasifler dahil.</summary>
    public virtual async Task<List<TListDto>> GetPickerListAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var rows = await AsyncExecuter.ToListAsync(
                (await Repository.GetQueryableAsync()).Where(BuildVisibilityPredicate()));

            var orders = await GetUnitOrdersAsync(rows.Select(FollowingUnitIdOf));
            var orderedEntities = rows.OrderBy(CodeOf, StringComparer.OrdinalIgnoreCase).ToList();
            var dtos = orderedEntities.Select(MapListWithIsGlobal).ToList();
            ApplyUnitCodes(orderedEntities, dtos, orders);
            await EnrichListAsync(orderedEntities, dtos);

            return dtos;
        }
    }

    protected override async Task<TGetDto> MapToGetOutputDtoAsync(TEntity entity)
    {
        var dto = await base.MapToGetOutputDtoAsync(entity);
        dto.FollowingUnitCode = await ResolveUnitCodeAsync(FollowingUnitIdOf(entity));
        return dto;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>FollowingUnit'in sıralama bilgisi: (GlobalRank: host=0, AlwaysShowInBalance, DisplayOrder, Code).</summary>
    private async Task<Dictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)>>
        GetUnitOrdersAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (int, bool, int, string)>();
        }

        using (DataFilter.Disable<IMultiTenant>())
        {
            return (await AsyncExecuter.ToListAsync(
                    (await _unitRepository.GetQueryableAsync())
                        .Where(u => ids.Contains(u.Id))
                        .Select(u => new { u.Id, u.TenantId, u.AlwaysShowInBalance, u.DisplayOrder, u.Code })))
                .ToDictionary(
                    u => u.Id,
                    u => (u.TenantId == null ? 0 : 1, u.AlwaysShowInBalance, u.DisplayOrder, u.Code ?? string.Empty));
        }
    }

    private void ApplyUnitCodes(
        IReadOnlyList<TEntity> entities,
        IReadOnlyList<TListDto> dtos,
        IReadOnlyDictionary<Guid, (int Global, bool AlwaysShow, int DisplayOrder, string Code)> orders)
    {
        // dtos[i], entities[i]'den map'lendi — sıra birebir.
        for (var i = 0; i < dtos.Count; i++)
        {
            if (orders.TryGetValue(FollowingUnitIdOf(entities[i]), out var v))
            {
                dtos[i].FollowingUnitCode = v.Code;
            }
        }
    }

    private async Task<string?> ResolveUnitCodeAsync(Guid unitId)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync()).Where(u => u.Id == unitId).Select(u => u.Code));
        }
    }
}
