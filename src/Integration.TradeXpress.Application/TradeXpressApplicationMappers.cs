using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Accounts;
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
// IsGlobal (TenantId==null) AppService'te elle set edilir.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitToGetDtoMapper : MapperBase<CurrencyUnit, CurrencyUnitGetDto>
{
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(CurrencyUnitGetDto.IsSystem))]
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
// IsGlobal (TenantId==null), BaseCode/QuoteCode (FK→Code enrichment)
// AppService'te elle set edilir (entity'de karşılığı yok).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityToGetDtoMapper : MapperBase<Parity, ParityGetDto>
{
    [MapperIgnoreTarget(nameof(ParityGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ParityGetDto.IsSystem))]
    [MapperIgnoreTarget(nameof(ParityGetDto.BaseCode))]
    [MapperIgnoreTarget(nameof(ParityGetDto.QuoteCode))]
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

// ── GetDto → Create/Update (PersistentCoordinator.CommitAsync; agnostic EntityEditForm save yolu) ──
// Coordinator IObjectMapper.Map<GetDto,Create/UpdateDto> çağırır; Mapperly bu eşlemeleri ister (fallback YOK).

[Mapper] public partial class CurrencyUnitGetToCreateMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitCreateDto>
{
    public override partial CurrencyUnitCreateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitCreateDto destination);
}
[Mapper] public partial class CurrencyUnitGetToUpdateMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitUpdateDto>
{
    public override partial CurrencyUnitUpdateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitUpdateDto destination);
}

[Mapper] public partial class CountryGetToCreateMapper : MapperBase<CountryGetDto, CountryCreateDto>
{
    public override partial CountryCreateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryCreateDto destination);
}
[Mapper] public partial class CountryGetToUpdateMapper : MapperBase<CountryGetDto, CountryUpdateDto>
{
    public override partial CountryUpdateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryUpdateDto destination);
}

[Mapper] public partial class CashGetToCreateMapper : MapperBase<CashGetDto, CashCreateDto>
{
    public override partial CashCreateDto Map(CashGetDto source);
    public override partial void Map(CashGetDto source, CashCreateDto destination);
}
[Mapper] public partial class CashGetToUpdateMapper : MapperBase<CashGetDto, CashUpdateDto>
{
    public override partial CashUpdateDto Map(CashGetDto source);
    public override partial void Map(CashGetDto source, CashUpdateDto destination);
}

[Mapper] public partial class AccountGetToCreateMapper : MapperBase<AccountGetDto, AccountCreateDto>
{
    public override partial AccountCreateDto Map(AccountGetDto source);
    public override partial void Map(AccountGetDto source, AccountCreateDto destination);
}
[Mapper] public partial class AccountGetToUpdateMapper : MapperBase<AccountGetDto, AccountUpdateDto>
{
    public override partial AccountUpdateDto Map(AccountGetDto source);
    public override partial void Map(AccountGetDto source, AccountUpdateDto destination);
}
