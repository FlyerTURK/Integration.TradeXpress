using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Cashes;

/// <summary>
/// Cash CRUD. <b>Görünürlük</b> (CurrencyUnit gibi): host kataloğu (TenantId=null) HERKESE görünür + tenant
/// kendi kayıtlarını görür → multi-tenant filter disable + açık predicate <c>TenantId == null || == own</c>.
/// Tenant global (host) Cash'i düzenleyemez/silemez; kendi ekleyebilir. <b>FollowingUnit (para birimi) ZORUNLU</b>
/// ve görünürlük kapsamında (global ‖ own) olmalıdır.
/// </summary>
[Authorize(TradeXpressPermissions.Cashes.Default)]
public class CashAppService : TradeXpressAppService, ICashAppService
{
    private readonly IRepository<Cash, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (FollowingUnit kod/ad zenginleştirme + doğrulama)
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id", "FollowingUnitCode", "FollowingUnitName" };

    public CashAppService(
        IRepository<Cash, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IDataFilter dataFilter)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _dataFilter = dataFilter;
    }

    public virtual async Task<PagedResultDto<CashListDto>> GetListAsync(CashListRequestDto input)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var units = await _unitRepository.GetQueryableAsync();

            // FollowingUnitCode/Name → self-join (korelasyonlu alt-sorgu) ile GERÇEK kolon: server-side sort/filter/arama.
            var rows = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .Select(c => new CashListRow
                {
                    Id = c.Id,
                    TenantId = c.TenantId,
                    Code = c.Code,
                    Name = c.Name,
                    IsActive = c.IsActive,
                    FollowingUnitId = c.FollowingUnitId,
                    FollowingUnitCode = units.Where(u => u.Id == c.FollowingUnitId).Select(u => u.Code).FirstOrDefault(),
                    FollowingUnitName = units.Where(u => u.Id == c.FollowingUnitId).Select(u => u.Name).FirstOrDefault(),
                })
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(rows);
            var items = await AsyncExecuter.ToListAsync(rows.Skip(input.SkipCount).Take(input.MaxResultCount));

            return new PagedResultDto<CashListDto>(totalCount, items.Select(ToListDto).ToList());
        }
    }

    public virtual async Task<CashGetDto> GetAsync(Guid id) => await ToGetDtoAsync(await GetInScopeAsync(id));

    [Authorize(TradeXpressPermissions.Cashes.Create)]
    public virtual async Task<CashGetDto> CreateAsync(CashCreateDto input)
    {
        var followingUnitId = await ResolveFollowingUnitAsync(input.FollowingUnitId);

        // TenantId otomatik atanır (ABP IMultiTenant): host→null (global), tenant→kendi.
        var entity = new Cash(input.Code, input.Name, followingUnitId);
        entity.SetDescription(input.Description);

        await _repository.InsertAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Cashes.Update)]
    public virtual async Task<CashGetDto> UpdateAsync(Guid id, CashUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        var followingUnitId = await ResolveFollowingUnitAsync(input.FollowingUnitId);

        entity.SetName(input.Name);
        entity.SetFollowingUnit(followingUnitId);
        entity.SetDescription(input.Description);
        entity.SetActive(input.IsActive);

        await _repository.UpdateAsync(entity, autoSave: true);
        return await ToGetDtoAsync(entity);
    }

    [Authorize(TradeXpressPermissions.Cashes.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    public virtual async Task<List<CashListDto>> GetPickerListAsync()
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var units    = await _unitRepository.GetQueryableAsync();
            var cashes   = await _repository.GetQueryableAsync();

            var rows = await AsyncExecuter.ToListAsync(
                from c in cashes
                where c.TenantId == null || c.TenantId == tenantId
                join u in units on c.FollowingUnitId equals u.Id into uj
                from u in uj.DefaultIfEmpty()
                orderby (u == null ? 0 : (u.TenantId == null ? 0 : 1)),
                        (u == null ? false : u.AlwaysShowInBalance) descending,
                        (u == null ? 0 : u.DisplayOrder),
                        (u == null ? string.Empty : u.Code),
                        c.Code
                select new CashListRow
                {
                    Id                = c.Id,
                    TenantId          = c.TenantId,
                    Code              = c.Code,
                    Name              = c.Name,
                    IsActive          = c.IsActive,
                    FollowingUnitId   = c.FollowingUnitId,
                    FollowingUnitCode = u == null ? null : u.Code,
                    FollowingUnitName = u == null ? null : u.Name,
                });

            return rows.Select(ToListDto).ToList();
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Id'yi görünürlük scope'unda (global + kendi) çeker; yoksa EntityNotFound.</summary>
    private async Task<Cash> GetInScopeAsync(Guid id)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var entity = await AsyncExecuter.FirstOrDefaultAsync(
                (await _repository.GetQueryableAsync())
                    .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId)));

            return entity ?? throw new EntityNotFoundException(typeof(Cash), id);
        }
    }

    /// <summary>Tenant, global (host) Cash'i düzenleyemez/silemez — yalnız host yönetir.</summary>
    private void EnsureEditable(Cash entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:Cash:CannotDeleteGlobalAsTenant"
                : "TradeXpress:Cash:CannotEditGlobalAsTenant");
        }
    }

    /// <summary>FollowingUnit zorunlu; görünürlük kapsamında (global ‖ own) var olmalı. Geçerli Id'yi döndürür.</summary>
    private async Task<Guid> ResolveFollowingUnitAsync(Guid? followingUnitId)
    {
        if (followingUnitId is not { } id || id == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Cash:FollowingUnitRequired");
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var exists = await AsyncExecuter.AnyAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == id && (u.TenantId == null || u.TenantId == tenantId)));

            if (!exists)
            {
                throw new EntityNotFoundException(typeof(CurrencyUnit), id);
            }
        }

        return id;
    }

    private static CashListDto ToListDto(CashListRow r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        Name = r.Name,
        FollowingUnitId = r.FollowingUnitId,
        FollowingUnitCode = r.FollowingUnitCode,
        FollowingUnitName = r.FollowingUnitName,
        IsActive = r.IsActive,
        IsGlobal = r.TenantId == null,
    };

    private async Task<CashGetDto> ToGetDtoAsync(Cash c)
    {
        string? followingCode;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            followingCode = await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == c.FollowingUnitId)
                    .Select(u => u.Code));
        }

        return new CashGetDto
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name,
            FollowingUnitId = c.FollowingUnitId,
            FollowingUnitCode = followingCode,
            Description = c.Description,
            IsActive = c.IsActive,
            IsGlobal = c.TenantId == null,
        };
    }

    // Liste projeksiyonu: Cash + self-join'lenmiş FollowingUnitCode/Name (gerçek string kolon → server-side sort/filter/arama).
    private sealed class CashListRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public Guid FollowingUnitId { get; set; }
        public string? FollowingUnitCode { get; set; }
        public string? FollowingUnitName { get; set; }
    }
}
