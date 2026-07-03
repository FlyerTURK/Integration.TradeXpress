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
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Financials.Parities;

/// <summary>
/// Parite CRUD. Görünürlük/guard <see cref="HostCatalogCrudAppService{TEntity,TGetDto,TListDto,TListRequest,TCreateInput,TUpdateInput}"/>
/// tabanından (host kataloğu + tenant kendi pariteleri; tenant global'i düzenleyemez/silemez).
///
/// <para>Çift = base/quote; oran saklanmaz (birim fiyatından türetilir). <b>Ters-çift kuralı</b>
/// (<see cref="ParityManager"/>): USDTRY varken TRYUSD oluşturulamaz — kapsam host‖own. Create tek
/// kapıdan (manager) geçer. Liste özel: Parity id-only olduğundan birim kodları join'le gerçek kolon
/// yapılır (server-side sort/filter/arama) — GetListAsync burada override.</para>
/// </summary>
[Authorize(TradeXpressPermissions.Parities.Default)]
public class ParityAppService
    : HostCatalogCrudAppService<Parity, ParityGetDto, ParityListDto, ParityListRequestDto, ParityCreateDto, ParityUpdateDto>,
      IParityAppService
{
    private readonly IRepository<CurrencyUnit, Guid> _currencyUnitRepository;
    private readonly ParityManager _parityManager;

    // Liste, Parity'yi CurrencyUnit'e join'leyip ParityListRow'a yansıtır → BaseCode/QuoteCode GERÇEK
    // string kolon olur; böylece kod ile sıralama/filtre/arama server-side çalışır (Parity id-only kalır).
    protected override ISet<string> AllowedListFields { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "IsActive", "DisplayOrder", "Id", "BaseCode", "QuoteCode" };

    // IsGlobal kolon değil (TenantId==null demek) → host-önce sıralaması için alias (projeksiyon satırında).
    private static readonly IReadOnlyDictionary<string, LambdaExpression> ListAliases =
        new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase)
        {
            ["IsGlobal"] = (Expression<Func<ParityListRow, bool>>)(r => r.TenantId == null),
        };

    public ParityAppService(
        IRepository<Parity, Guid> repository,
        IRepository<CurrencyUnit, Guid> currencyUnitRepository,
        ParityManager parityManager)
        : base(repository)
    {
        _currencyUnitRepository = currencyUnitRepository;
        _parityManager = parityManager;
        LocalizationResource = typeof(TradeXpressResource);
        CreatePolicyName = TradeXpressPermissions.Parities.Create;
        UpdatePolicyName = TradeXpressPermissions.Parities.Update;
        DeletePolicyName = TradeXpressPermissions.Parities.Delete;
    }

    protected override string EditGlobalErrorCode
    {
        get { return "TradeXpress:Parity:CannotEditGlobalAsTenant"; }
    }

    protected override string DeleteGlobalErrorCode
    {
        get { return "TradeXpress:Parity:CannotDeleteGlobalAsTenant"; }
    }

    protected override Expression<Func<Parity, string>> PickerOrderSelector
    {
        get { return x => x.Id.ToString(); }   // picker yok (sözleşmede GetPickerListAsync tanımlı değil)
    }

    public override async Task<PagedResultDto<ParityListDto>> GetListAsync(ParityListRequestDto input)
    {
        await CheckGetListPolicyAsync();

        using (DataFilter.Disable<IMultiTenant>())
        {
            if ((input.Sorts == null || input.Sorts.Count == 0) && string.IsNullOrWhiteSpace(input.Sorting))
            {
                input.Sorts = DefaultListSorts();
            }

            // Parity id-only (nav yok) → kodları join ile getir; ParityListRow'da BaseCode/QuoteCode gerçek
            // kolon olduğundan ApplyListRequest kod ile sıralama/filtre/arama'yı server-side uygular.
            var units = await _currencyUnitRepository.GetQueryableAsync();
            var rows = (await Repository.GetQueryableAsync())
                .Where(BuildVisibilityPredicate())
                .Join(units, p => p.BaseCurrencyUnitId, u => u.Id, (p, u) => new { p, baseCode = u.Code, baseName = u.Name })
                .Join(units, x => x.p.QuoteCurrencyUnitId, u => u.Id, (x, u) => new ParityListRow
                {
                    Id = x.p.Id,
                    TenantId = x.p.TenantId,
                    BaseCurrencyUnitId = x.p.BaseCurrencyUnitId,
                    QuoteCurrencyUnitId = x.p.QuoteCurrencyUnitId,
                    BaseCode = x.baseCode,
                    BaseName = x.baseName,
                    QuoteCode = u.Code,
                    QuoteName = u.Name,
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

    public override async Task<ParityGetDto> CreateAsync(ParityCreateDto input)
    {
        await CheckCreatePolicyAsync();

        // Tek create kapısı manager: ön-kontrol (ters/aynı çift, base==quote) + insert. TenantId'yi ABP atar.
        var entity = await _parityManager.CreateAsync(
            input.BaseCurrencyUnitId,
            input.QuoteCurrencyUnitId,
            input.IsActive,
            input.DisplayOrder,
            CurrentTenant.Id);

        return await MapToGetOutputDtoAsync(entity);
    }

    protected override Task<Parity> MapToEntityAsync(ParityCreateDto createInput)
    {
        // Create tamamen manager'a delege — bu yol asla çağrılmamalı (fail-fast).
        throw new InvalidOperationException("Parity create ParityManager üzerinden yapılır; CreateAsync override'ı kullanın.");
    }

    protected override Task MapToEntityAsync(ParityUpdateDto updateInput, Parity entity)
    {
        entity.SetActive(updateInput.IsActive);
        entity.SetDisplayOrder(updateInput.DisplayOrder);
        return Task.CompletedTask;
    }

    protected override async Task<ParityGetDto> MapToGetOutputDtoAsync(Parity entity)
    {
        var dto = await base.MapToGetOutputDtoAsync(entity);   // Mapperly + IsGlobal (IHostScoped)
        dto.IsSystem = entity.TenantId == null;

        var codes = await GetCodeMapAsync(new[] { entity.BaseCurrencyUnitId, entity.QuoteCurrencyUnitId });
        dto.BaseCode = codes.GetValueOrDefault(entity.BaseCurrencyUnitId, string.Empty);
        dto.QuoteCode = codes.GetValueOrDefault(entity.QuoteCurrencyUnitId, string.Empty);
        return dto;
    }

    // ── Yardımcılar ──

    // Varsayılan sıra: host (global) önce → DisplayOrder artan → Id (deterministik tie-break).
    private static List<SortField> DefaultListSorts()
    {
        return new List<SortField>
        {
            new() { Field = "IsGlobal",     Descending = true  },
            new() { Field = "DisplayOrder", Descending = false },
            new() { Field = "Id",           Descending = false },
        };
    }

    /// <summary>Verilen birim id'leri için Id→Code haritası (global + tenant birimleri).</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> GetCodeMapAsync(IEnumerable<Guid> unitIds)
    {
        var ids = unitIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        using (DataFilter.Disable<IMultiTenant>())
        {
            var units = await AsyncExecuter.ToListAsync(
                (await _currencyUnitRepository.GetQueryableAsync()).Where(u => ids.Contains(u.Id)));
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    private static ParityListDto ToListDto(ParityListRow r)
    {
        return new ParityListDto
        {
            Id = r.Id,
            BaseCurrencyUnitId = r.BaseCurrencyUnitId,
            QuoteCurrencyUnitId = r.QuoteCurrencyUnitId,
            BaseCode = r.BaseCode,
            BaseName = r.BaseName,
            QuoteCode = r.QuoteCode,
            QuoteName = r.QuoteName,
            IsActive = r.IsActive,
            IsSystem = r.TenantId == null,
            IsGlobal = r.TenantId == null,
            DisplayOrder = r.DisplayOrder,
        };
    }

    // Liste projeksiyonu: Parity + join'lenmiş birim kodları. BaseCode/QuoteCode gerçek string kolon
    // olduğundan ApplyListRequest sıralama/filtre/arama'yı server-side uygular (Parity id-only kalır).
    private sealed class ParityListRow
    {
        public Guid Id { get; set; }
        public Guid? TenantId { get; set; }
        public Guid BaseCurrencyUnitId { get; set; }
        public Guid QuoteCurrencyUnitId { get; set; }
        public string BaseCode { get; set; } = string.Empty;
        public string BaseName { get; set; } = string.Empty;
        public string QuoteCode { get; set; } = string.Empty;
        public string QuoteName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }
}
