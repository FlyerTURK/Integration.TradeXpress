using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Integration.Framework;
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

namespace Integration.TradeXpress.Financials.CurrencyUnits;

/// <summary>
/// CurrencyUnit CRUD. <b>Görünürlük kuralı:</b> host kataloğu (TenantId=null) HERKESE
/// görünür + tenant kendi birimlerini görür. ABP'nin standart multi-tenant filter'ı
/// tenant'a yalnız kendi verisini gösterirdi (global kataloğu gizlerdi); bu yüzden
/// filter <b>disable</b> edilip açık predicate uygulanır: <c>TenantId == null || == CurrentTenant.Id</c>.
/// (Host'ta CurrentTenant.Id=null → predicate yalnız global'i verir; başka tenant görünmez.)
///
/// <para>Tenant, global (host) birimi düzenleyemez/silemez — yalnız okur. Kendi kâr marjını
/// ayrı <c>TenantCurrencyMargin</c> katmanında tutacak (sonraki increment).</para>
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnits.Default)]
public class CurrencyUnitAppService : TradeXpressAppService, ICurrencyUnitAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _repository;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase)
        { "Code", "Name", "Type", "IsActive", "DisplayOrder", "Id", "AlwaysShowInBalance", "FollowingUnitCode" };

    // Sort/filter alias'ı: IsGlobal kolon değil (TenantId==null demek) → host-önce sıralaması (projeksiyon satırında).
    private static readonly IReadOnlyDictionary<string, LambdaExpression> ListAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase)
        {
            ["IsGlobal"] = (Expression<Func<CurrencyUnitListRow, bool>>)(r => r.TenantId == null),
        };

    // Kullanıcı kolon sıralamadığında CurrencyUnit standart sıralaması:
    // Host (global) önce → Bakiyede gösterilenler önce → Sıra artan → Code artan (sonra Id tie-breaker).
    private static List<SortField> DefaultListSorts() => new()
    {
        new() { Field = "IsGlobal",            Descending = true  },
        new() { Field = "AlwaysShowInBalance", Descending = true  },
        new() { Field = "DisplayOrder",        Descending = false },
        new() { Field = "Code",                Descending = false },
    };

    public CurrencyUnitAppService(IRepository<CurrencyUnit, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<PagedResultDto<CurrencyUnitListDto>> GetListAsync(CurrencyUnitListRequestDto input)
    {
        // Multi-tenant filter'ı kapat, görünürlüğü kendimiz belirle (global + kendi).
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;

            // Kullanıcı bir sıralama vermediyse CurrencyUnit'e özel standart sıralamayı uygula.
            if ((input.Sorts == null || input.Sorts.Count == 0) && string.IsNullOrWhiteSpace(input.Sorting))
                input.Sorts = DefaultListSorts();

            // FollowingUnitCode enrichment'tı → self-join (korelasyonlu alt-sorgu, FollowingUnitId nullable)
            // ile GERÇEK kolon yap: kod ile sort/filter/arama server-side çalışsın.
            var all = await _repository.GetQueryableAsync();
            var rows = (await _repository.GetQueryableAsync())
                .Where(x => x.TenantId == null || x.TenantId == tenantId)
                .Select(c => new CurrencyUnitListRow
                {
                    Id = c.Id,
                    TenantId = c.TenantId,
                    Code = c.Code,
                    Name = c.Name,
                    Type = c.Type,
                    AlwaysShowInBalance = c.AlwaysShowInBalance,
                    DisplayOrder = c.DisplayOrder,
                    IsActive = c.IsActive,
                    FollowingUnitId = c.FollowingUnitId,
                    FollowingUnitCode = c.FollowingUnitId == null
                        ? null
                        : all.Where(f => f.Id == c.FollowingUnitId).Select(f => f.Code).FirstOrDefault(),
                    FollowingMarginType = c.FollowingUnitId == null ? (MarginType?)null : c.FollowingMargin!.Type,
                    FollowingMarginValue = c.FollowingUnitId == null ? (decimal?)null : c.FollowingMargin!.Value,
                })
                .ApplyListRequest(input, AllowedListFields, ListAliases);

            var totalCount = await AsyncExecuter.CountAsync(rows);
            var items = await AsyncExecuter.ToListAsync(
                rows.ApplyPaging(input));

            return new PagedResultDto<CurrencyUnitListDto>(totalCount, items.Select(ToListDto).ToList());
        }
    }

    public virtual async Task<CurrencyUnitGetDto> GetAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Create)]
    public virtual async Task<CurrencyUnitGetDto> CreateAsync(CurrencyUnitCreateDto input)
    {
        // Benzersizlik ÖN-kontrolü (Update ile simetrik + aynı scope): (TenantId, Code) unique index'iyle hizalı,
        // ham DB çakışması yerine dostane hata. Ambient multi-tenant filter kapsamı belirler (host→global, tenant→kendi).
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(CurrencyUnit.Code), CurrencyConsts.CodeMinLength, CurrencyConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(normalizedCode, Guid.Empty);

        // TenantId otomatik atanır (ABP IMultiTenant): host→null (global), tenant→kendi.
        var entity = new CurrencyUnit(
            input.Code,
            input.Name,
            input.Type,
            displayOrder: input.DisplayOrder);

        entity.SetDescription(input.Description);
        entity.SetAlwaysShowInBalance(input.AlwaysShowInBalance);
        await ApplyFollowingAsync(entity, input.FollowingUnitId, input.FollowingMarginType, input.FollowingMarginValue);

        await _repository.InsertAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Update)]
    public virtual async Task<CurrencyUnitGetDto> UpdateAsync(Guid id, CurrencyUnitUpdateDto input)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity);

        await ApplyCodeChangeAsync(entity, input.Code);

        entity.SetName(input.Name);
        entity.SetDescription(input.Description);
        entity.SetDisplayOrder(input.DisplayOrder);
        entity.SetAlwaysShowInBalance(input.AlwaysShowInBalance);
        entity.SetActive(input.IsActive);

        await ApplyFollowingAsync(entity, input.FollowingUnitId, input.FollowingMarginType, input.FollowingMarginValue);

        await _repository.UpdateAsync(entity, autoSave: true);
        return ToGetDto(entity);
    }

    [Authorize(TradeXpressPermissions.CurrencyUnits.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await GetInScopeAsync(id);
        EnsureEditable(entity, isDelete: true);

        await _repository.DeleteAsync(entity, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Id'yi görünürlük scope'unda (global + kendi) çeker; yoksa EntityNotFound.</summary>
    private async Task<CurrencyUnit> GetInScopeAsync(Guid id)
    {
        using (DataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var query = (await _repository.GetQueryableAsync())
                .Where(x => x.Id == id && (x.TenantId == null || x.TenantId == tenantId));

            var entity = await AsyncExecuter.FirstOrDefaultAsync(query);
            if (entity is null)
                throw new EntityNotFoundException(typeof(CurrencyUnit), id);
            return entity;
        }
    }

    /// <summary>Kod değişikliği (ürün kuralı 2026-07-04): TENANT birimlerinde kod düzenlenebilir; HOST
    /// (TenantId==null) biriminin kodu KİMSE tarafından değiştirilemez (Cash seed'i, Country.DefaultCurrencyCode
    /// ve takip/parite türetmeleri host koduna bağlı) → <c>HostCodeLocked</c>. Değiştiyse tenant scope'unda
    /// benzersizlik doğrulanır ((TenantId, Code) unique index'iyle hizalı; dostane hata).</summary>
    private async Task ApplyCodeChangeAsync(CurrencyUnit entity, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(entity.Code), CurrencyConsts.CodeMinLength, CurrencyConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, entity.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        if (entity.TenantId == null)
        {
            throw new BusinessException("TradeXpress:CurrencyUnit:HostCodeLocked");
        }

        await EnsureCodeUniqueAsync(normalizedCode, entity.Id);
        entity.SetCode(normalizedCode);
    }

    /// <summary>Code benzersizliği ((TenantId, Code) unique index'iyle hizalı; kapsamı ambient multi-tenant
    /// filter belirler — host→global, tenant→kendi). Create'te <paramref name="excludeId"/>=Guid.Empty
    /// (kendisi yok), Update'te entity.Id (kendisi hariç). Dostane BusinessException — ham DB çakışmasını önler.</summary>
    private async Task EnsureCodeUniqueAsync(string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(u => u.Id != excludeId && u.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:CurrencyUnit:CodeAlreadyExists");
        }
    }

    /// <summary>Tenant, global (host) birimi düzenleyemez/silemez — yalnız host yönetir.</summary>
    private void EnsureEditable(CurrencyUnit entity, bool isDelete = false)
    {
        if (entity.TenantId == null && CurrentTenant.Id != null)
        {
            throw new BusinessException(isDelete
                ? "TradeXpress:CurrencyUnit:CannotDeleteGlobalAsTenant"
                : "TradeXpress:CurrencyUnit:CannotEditGlobalAsTenant");
        }
    }

    /// <summary>Takip ilişkisini kurar; tek-seviye kuralını repo ile doğrular (parent kendisi takip-eden olamaz).</summary>
    private async Task ApplyFollowingAsync(CurrencyUnit entity, Guid? followingUnitId, MarginType? marginType, decimal? marginValue)
    {
        if (followingUnitId is null)
        {
            entity.SetFollowing(null, null);
            return;
        }

        if (marginType is null || marginValue is null)
            throw new BusinessException("TradeXpress:CurrencyUnit:FollowingMarginRequired");

        var parent = await GetInScopeAsync(followingUnitId.Value);
        if (parent.IsFollowing)
            throw new BusinessException("TradeXpress:CurrencyUnit:FollowMustBeSingleLevel");

        entity.SetFollowing(followingUnitId, new MarginSetting(marginType.Value, marginValue.Value));
    }

    private static CurrencyUnitListDto ToListDto(CurrencyUnitListRow r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        Name = r.Name,
        Type = r.Type,
        AlwaysShowInBalance = r.AlwaysShowInBalance,
        DisplayOrder = r.DisplayOrder,
        IsActive = r.IsActive,
        IsGlobal = r.TenantId == null,
        IsSystem = r.TenantId == null,
        FollowingUnitId = r.FollowingUnitId,
        FollowingUnitCode = r.FollowingUnitCode,
        FollowingMarginType = r.FollowingMarginType,
        FollowingMarginValue = r.FollowingMarginValue,
    };

    // Liste projeksiyonu: CurrencyUnit + self-join'lenmiş FollowingUnitCode (gerçek string kolon →
    // server-side sort/filter/arama). Owned VO (FollowingMargin) FollowingUnitId varken doludur.
    private sealed class CurrencyUnitListRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public CurrencyUnitType Type { get; set; }
        public bool AlwaysShowInBalance { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; }
        public Guid? FollowingUnitId { get; set; }
        public string? FollowingUnitCode { get; set; }
        public MarginType? FollowingMarginType { get; set; }
        public decimal? FollowingMarginValue { get; set; }
    }

    private CurrencyUnitGetDto ToGetDto(CurrencyUnit e)
    {
        var dto = ObjectMapper.Map<CurrencyUnit, CurrencyUnitGetDto>(e);
        dto.IsGlobal = e.TenantId == null;
        dto.IsSystem = e.TenantId == null;
        return dto;
    }
}
