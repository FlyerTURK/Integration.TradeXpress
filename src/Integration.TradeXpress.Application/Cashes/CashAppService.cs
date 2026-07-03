using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework.Application;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Cashes;

/// <summary>
/// Cash CRUD. Görünürlük/guard <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından (host kataloğu + tenant kendi kayıtları). <b>FollowingUnit (para birimi) ZORUNLU</b> ve görünürlük
/// kapsamında (global ‖ own) olmalıdır. Liste/picker özel: FollowingUnitCode/Name self-join ile gerçek kolon
/// (server-side sort/filter/arama) — bu yüzden GetListAsync/picker burada override edilir.
/// </summary>
[Authorize(TradeXpressPermissions.Cashes.Default)]
public class CashAppService
    : HostCatalogCrudAppService<Cash, CashGetDto, CashListDto, CashListRequestDto, CashCreateDto, CashUpdateDto>,
      ICashAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;  // yalnız OKUMA (FollowingUnit kod/ad zenginleştirme + doğrulama)

    public CashAppService(
        IRepository<Cash, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository)
        : base(repository)
    {
        _unitRepository = unitRepository;
        LocalizationResource = typeof(TradeXpressResource);
        CreatePolicyName = TradeXpressPermissions.Cashes.Create;
        UpdatePolicyName = TradeXpressPermissions.Cashes.Update;
        DeletePolicyName = TradeXpressPermissions.Cashes.Delete;
    }

    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsActive", "Id", "FollowingUnitCode", "FollowingUnitName" };

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Cash:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Cash:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Cash, string>> PickerOrderSelector
    {
        get { return x => x.Code; }   // kullanılmaz — picker aşağıda komple override (birim-bazlı kompozit sıralama)
    }

    public override async Task<PagedResultDto<CashListDto>> GetListAsync(CashListRequestDto input)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var units = await _unitRepository.GetQueryableAsync();

            // FollowingUnitCode/Name → self-join (korelasyonlu alt-sorgu) ile GERÇEK kolon: server-side sort/filter/arama.
            var rows = (await Repository.GetQueryableAsync())
                .Where(BuildVisibilityPredicate())
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

    public virtual async Task<List<CashListDto>> GetPickerListAsync()
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var units  = await _unitRepository.GetQueryableAsync();
            var cashes = (await Repository.GetQueryableAsync()).Where(BuildVisibilityPredicate());

            var rows = await AsyncExecuter.ToListAsync(
                from c in cashes
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

    protected override async Task<Cash> MapToEntityAsync(CashCreateDto createInput)
    {
        var followingUnitId = await ResolveFollowingUnitAsync(createInput.FollowingUnitId);

        // TenantId otomatik atanır (ABP IMultiTenant): host→null (global), tenant→kendi.
        var entity = new Cash(createInput.Code, createInput.Name, followingUnitId);
        entity.SetDescription(createInput.Description);
        return entity;
    }

    protected override async Task MapToEntityAsync(CashUpdateDto updateInput, Cash entity)
    {
        var followingUnitId = await ResolveFollowingUnitAsync(updateInput.FollowingUnitId);

        entity.SetName(updateInput.Name);
        entity.SetFollowingUnit(followingUnitId);
        entity.SetDescription(updateInput.Description);
        entity.SetActive(updateInput.IsActive);
    }

    // Cash için Mapperly entity→DTO mapping yok; GetDto elle + FollowingUnitCode zenginleştirmesiyle kurulur.
    protected override async Task<CashGetDto> MapToGetOutputDtoAsync(Cash entity)
    {
        string? followingCode;
        using (DataFilter.Disable<IMultiTenant>())
        {
            followingCode = await AsyncExecuter.FirstOrDefaultAsync(
                (await _unitRepository.GetQueryableAsync())
                    .Where(u => u.Id == entity.FollowingUnitId)
                    .Select(u => u.Code));
        }

        return new CashGetDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            FollowingUnitId = entity.FollowingUnitId,
            FollowingUnitCode = followingCode,
            Description = entity.Description,
            IsActive = entity.IsActive,
            IsGlobal = entity.TenantId == null,
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>FollowingUnit zorunlu; görünürlük kapsamında (global ‖ own) var olmalı. Geçerli Id'yi döndürür.</summary>
    private async Task<Guid> ResolveFollowingUnitAsync(Guid? followingUnitId)
    {
        if (followingUnitId is not { } id || id == Guid.Empty)
        {
            throw new BusinessException("TradeXpress:Cash:FollowingUnitRequired");
        }

        using (DataFilter.Disable<IMultiTenant>())
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

    private static CashListDto ToListDto(CashListRow r)
    {
        return new CashListDto
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
