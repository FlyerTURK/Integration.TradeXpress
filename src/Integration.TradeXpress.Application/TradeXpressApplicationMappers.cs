using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Volo.Abp.TenantManagement;

namespace Integration.TradeXpress;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantToTenantGetDtoMapper : MapperBase<Tenant, TenantGetDto>
{
    public override partial TenantGetDto Map(Tenant source);
    public override partial void Map(Tenant source, TenantGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantToTenantListDtoMapper : MapperBase<Tenant, TenantListDto>
{
    public override partial TenantListDto Map(Tenant source);
    public override partial void Map(Tenant source, TenantListDto destination);
}

// ── CurrencyUnit ──────────────────────────────────────────────────────────────
// Margin VO'ları otomatik düzleştirilir (MarginOnBuy.Type → MarginOnBuyType).
// IsGlobal (TenantId==null) ve PageIndex AppService'te elle set edilir.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToGetDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitGetDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsSystem))]
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.PageIndex))]
    public override partial CurrencyUnitGetDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToListDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitListDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(CurrencyUnitListDto.IsSystem))]
    public override partial CurrencyUnitListDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitListDto destination);
}

// ── Parity ──────────────────────────────────────────────────────────────────
// IsGlobal (TenantId==null), BaseCode/QuoteCode (FK→Code enrichment) ve PageIndex
// AppService'te elle set edilir (entity'de karşılığı yok).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityToGetDtoMapper : MapperBase<Parity, ParityGetDto>
{
    [MapperIgnoreTarget(nameof(ParityGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ParityGetDto.IsSystem))]
    [MapperIgnoreTarget(nameof(ParityGetDto.BaseCode))]
    [MapperIgnoreTarget(nameof(ParityGetDto.QuoteCode))]
    [MapperIgnoreTarget(nameof(ParityGetDto.PageIndex))]
    public override partial ParityGetDto Map(Parity source);
    public override partial void Map(Parity source, ParityGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityToListDtoMapper : MapperBase<Parity, ParityListDto>
{
    [MapperIgnoreTarget(nameof(ParityListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ParityListDto.IsSystem))]
    [MapperIgnoreTarget(nameof(ParityListDto.BaseCode))]
    [MapperIgnoreTarget(nameof(ParityListDto.QuoteCode))]
    public override partial ParityListDto Map(Parity source);
    public override partial void Map(Parity source, ParityListDto destination);
}
