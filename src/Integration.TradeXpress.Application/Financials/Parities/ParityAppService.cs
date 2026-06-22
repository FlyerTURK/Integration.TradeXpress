using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite CRUD. <b>Görünürlük:</b> host kataloğu (TenantId=null) HERKESE görünür + tenant kendi
/// paritelerini görür — ABP multi-tenant filter disable + açık predicate (<c>TenantId==null||==tenant</c>),
/// <see cref="CurrencyUnitAppService"/> ile bire bir. Tenant global pariteyi düzenleyemez/silemez (salt-okur).
///
/// <para>Çift = base/quote; oran saklanmaz (birim fiyatından türetilir). <b>Ters-çift kuralı</b>
/// (<see cref="ParityManager"/>): USDTRY varken TRYUSD oluşturulamaz — kapsam host‖own.</para>
/// </summary>
[Authorize(TradeXpressPermissions.Parities.Default)]
public class ParityAppService : TradeXpressAppService, IParityAppService
{
    private readonly IRepository<Parity, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly ParityManager _parityManager;

    // Liste, Parity'yi CurrencyUnit'e join'leyip ParityListRow'a yansıtır → BaseCode/QuoteCode GERÇEK
    // string kolon olur; böylece kod ile sıralama/filtre/arama server-side çalışır (Parity id-only kalır).
    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase)
        { "IsActive", "DisplayOrder", "Id", "BaseCode", "QuoteCode" };

    // IsGlobal kolon değil (TenantId==null demek) → host-önce sıralaması için alias (projeksiyon satırında).
    private static readonly IReadOnlyDictionary<string, LambdaExpression> ListAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase)
        {
            ["IsGlobal"] = (Expression<Func<ParityListRow, bool>>)(r => r.TenantId == null),
        };

    // Varsayılan sıra: host (global) önce → DisplayOrder artan → Id (deterministik tie-break).
    private static List<SortField> DefaultListSorts() => new()
    {
        new() { Field = "IsGlobal",     Descending = true  },
        new() { Field = "DisplayOrder", Descending = false },
        new() { Field = "Id",           Descending = false },
    };

    public ParityAppService(
        IRepository<Parity, Guid> repository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        ParityManager parityManager)
    {
        _repository = repository;
        _currencyUnitRepository = currencyUnitRepository;
        _parityManager = parityManager;
    }

    public virtual async Task<PagedResultDto<ParityListDto>> GetListAsync(ParityListRequestDto input)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;

            if ((input.Sorts == null || input.Sorts.Count == 0) && string.IsNullOrWhiteSpace(input.Sorting))
                input.Sorts = DefaultListSorts();

            // Parity id-only (nav yok) → kodları join ile getir; ParityListRow'da BaseCode/QuoteCode gerçek
            // kolon olduğundan ApplyListRequest kod ile sıralama/filtre/arama'yı server-side uygular.
            var units = await _currencyUnitRepository.GetQueryableAsync();
            var rows = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .Join(units, p => p.BaseCurrencyUnitId, u => u.Id, (p, u) => new { p, baseCode = u.Code })
                .Join(units, x => x.p.QuoteCurrencyUnitId, u => u.Id, (x, u) => new ParityListRow
                {
                    Id = x.p.Id,
                    TenantId = x.p.TenantId,
                    BaseCurrencyUnitId = x.p.BaseCurrencyUnitId,
                    QuoteCurrencyUnitId = x.p.QuoteCurrencyUnitId,
                    BaseCode = x.baseCode,
                    QuoteCode = u.Code,
                    IsActive = x.p.IsActive,
                    DisplayOrder = x.p.DisplayOrder,
                })
                .ApplyListRequest(input, AllowedListFields, ListAliases);

            var totalCount = await AsyncExecuter.CountAsync(rows);
            var items = await AsyncExecuter.ToListAsync(
                rows.Skip(input.SkipCount).Take(input.MaxResultCount));

            return new PagedResultDto<ParityListDto>(totalCount, items.Select(ToListDto).ToList());
        }
    }

    public virtual async Task<ParityGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        var codes = await GetCodeMapAsync(new[] { entity.BaseCurrencyUnitId, entity.QuoteCurrencyUnitId });
        return ToGetDto(entity, codes);
    }

    [Authorize(TradeXpressPermissions.Parities.Create)]
    public virtual async Task<ParityGetDto> CreateAsync(ParityCreateDto input)
    {
        // Tek create kapısı manager: ön-kontrol (ters/aynı çift, base==quote) + insert. TenantId'yi ABP atar.
        var entity = await _parityManager.CreateAsync(
            input.BaseCurrencyUnitId,
            input.QuoteCurrencyUnitId,
            input.IsActive,
            input.DisplayOrder,
            CurrentTenant.Id);

        var codes = await GetCodeMapAsync(new[] { entity.BaseCurrencyUnitId, entity.QuoteCurrencyUnitId });
        return ToGetDto(entity, codes);
    }

    [Authorize(TradeXpressPermissions.Parities.Update)]
    public virtual async Task<ParityGetDto> UpdateAsync(Guid id, ParityUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        entity.SetActive(input.IsActive);
        entity.SetDisplayOrder(input.DisplayOrder);

        await _repository.UpdateAsync(entity, autoSave: true);

        var codes = await GetCodeMapAsync(new[] { entity.BaseCurrencyUnitId, entity.QuoteCurrencyUnitId });
        return ToGetDto(entity, codes);
    }

    [Authorize(TradeXpressPermissions.Parities.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── Yardımcılar ──

    /// <summary>Kayıt görünür kapsamda (global + kendi tenant) mı? Değilse EntityNotFound.</summary>
    private async Task<Parity> GetInScopeAsync(Guid id)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));

            if (entity is null)
                throw new EntityNotFoundException(typeof(Parity), id);
            return entity;
        }
    }

    /// <summary>Tenant, global (host) pariteyi düzenleyemez/silemez — yalnız host yönetir.</summary>
    private void EnsureEditable(Parity entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Parity:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Parity:CannotEditGlobalAsTenant");
        }
    }

    /// <summary>Verilen birim id'leri için Id→Code haritası (global + tenant birimleri).</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> GetCodeMapAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        using (DataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(u => ids.Contains(u.Id)));
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    private static ParityListDto ToListDto(ParityListRow r) => new()
    {
        Id = r.Id,
        BaseCurrencyUnitId = r.BaseCurrencyUnitId,
        QuoteCurrencyUnitId = r.QuoteCurrencyUnitId,
        BaseCode = r.BaseCode,
        QuoteCode = r.QuoteCode,
        IsActive = r.IsActive,
        IsSystem = r.TenantId == null,
        IsGlobal = r.TenantId == null,
        DisplayOrder = r.DisplayOrder,
    };

    // Liste projeksiyonu: Parity + join'lenmiş birim kodları. BaseCode/QuoteCode gerçek string kolon
    // olduğundan ApplyListRequest sıralama/filtre/arama'yı server-side uygular (Parity id-only kalır).
    private sealed class ParityListRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public Guid BaseCurrencyUnitId { get; set; }
        public Guid QuoteCurrencyUnitId { get; set; }
        public string BaseCode { get; set; } = string.Empty;
        public string QuoteCode { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }

    private ParityGetDto ToGetDto(Parity e, IReadOnlyDictionary<Guid, string> codes)
    {
        var dto = ObjectMapper.Map<Parity, ParityGetDto>(e);
        dto.IsGlobal = e.TenantId == null;
        dto.IsSystem = e.TenantId == null;
        dto.BaseCode = codes.GetValueOrDefault(e.BaseCurrencyUnitId, string.Empty);
        dto.QuoteCode = codes.GetValueOrDefault(e.QuoteCurrencyUnitId, string.Empty);
        return dto;
    }
}
