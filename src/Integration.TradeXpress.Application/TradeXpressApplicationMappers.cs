using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Currencies;
using Volo.Abp.TenantManagement;
using System.Collections.Generic;

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
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.PageIndex))]
    public override partial CurrencyUnitGetDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToListDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitListDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitListDto.IsGlobal))]
    public override partial CurrencyUnitListDto Map(CurrencyUnit source);
    public override partial void Map(CurrencyUnit source, CurrencyUnitListDto destination);
}
