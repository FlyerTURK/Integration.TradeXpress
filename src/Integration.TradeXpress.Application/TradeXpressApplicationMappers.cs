using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Cashes;
using Integration.TradeXpress.Services;
using Integration.TradeXpress.Futures;
using Integration.TradeXpress.Scraps;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Stones;
using Integration.TradeXpress.Jewelries;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.AssayOffices;
using Integration.TradeXpress.Scheduling;
using Integration.TradeXpress.Authorization;
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

[Mapper] public partial class AssayOfficeGetToCreateMapper : MapperBase<AssayOfficeGetDto, AssayOfficeCreateDto>
{
    public override partial AssayOfficeCreateDto Map(AssayOfficeGetDto source);
    public override partial void Map(AssayOfficeGetDto source, AssayOfficeCreateDto destination);
}
[Mapper] public partial class AssayOfficeGetToUpdateMapper : MapperBase<AssayOfficeGetDto, AssayOfficeUpdateDto>
{
    public override partial AssayOfficeUpdateDto Map(AssayOfficeGetDto source);
    public override partial void Map(AssayOfficeGetDto source, AssayOfficeUpdateDto destination);
}

// ── SchedulerAppointment ──────────────────────────────────────────────────────
// Entity↔DTO alan adları birebir → otomatik eşleme (CompanyId/TenantId/audit = source-only, hedefte yok).

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class SchedulerAppointmentToDtoMapper : MapperBase<SchedulerAppointment, SchedulerAppointmentDto>
{
    public override partial SchedulerAppointmentDto Map(SchedulerAppointment source);
    public override partial void Map(SchedulerAppointment source, SchedulerAppointmentDto destination);
}

// ── AssayOffice (entity→DTO: statik mapper anti-pattern'inden Mapperly'ye çevrildi) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AssayOfficeToGetDtoMapper : MapperBase<AssayOffice, AssayOfficeGetDto>
{
    public override partial AssayOfficeGetDto Map(AssayOffice source);
    public override partial void Map(AssayOffice source, AssayOfficeGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class AssayOfficeToListDtoMapper : MapperBase<AssayOffice, AssayOfficeListDto>
{
    public override partial AssayOfficeListDto Map(AssayOffice source);
    public override partial void Map(AssayOffice source, AssayOfficeListDto destination);
}

// ── Service (statik mapper → Mapperly; IsGlobal = TenantId==null AppService'te elle set, CurrencyUnit deseni) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ServiceToGetDtoMapper : MapperBase<Service, ServiceGetDto>
{
    [MapperIgnoreTarget(nameof(ServiceGetDto.IsGlobal))]
    public override partial ServiceGetDto Map(Service source);
    public override partial void Map(Service source, ServiceGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ServiceToListDtoMapper : MapperBase<Service, ServiceListDto>
{
    [MapperIgnoreTarget(nameof(ServiceListDto.IsGlobal))]
    public override partial ServiceListDto Map(Service source);
    public override partial void Map(Service source, ServiceListDto destination);
}

// ── Scrap (statik mapper → Mapperly; IsGlobal + FollowingUnitCode AppService'te/ApplyUnitCodes ile set) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ScrapToGetDtoMapper : MapperBase<Scrap, ScrapGetDto>
{
    [MapperIgnoreTarget(nameof(ScrapGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ScrapGetDto.FollowingUnitCode))]
    public override partial ScrapGetDto Map(Scrap source);
    public override partial void Map(Scrap source, ScrapGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ScrapToListDtoMapper : MapperBase<Scrap, ScrapListDto>
{
    [MapperIgnoreTarget(nameof(ScrapListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(ScrapListDto.FollowingUnitCode))]
    public override partial ScrapListDto Map(Scrap source);
    public override partial void Map(Scrap source, ScrapListDto destination);
}

// ── Metal (Scrap deseni: IsGlobal + FollowingUnitCode ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MetalToGetDtoMapper : MapperBase<Metal, MetalGetDto>
{
    [MapperIgnoreTarget(nameof(MetalGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(MetalGetDto.FollowingUnitCode))]
    public override partial MetalGetDto Map(Metal source);
    public override partial void Map(Metal source, MetalGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class MetalToListDtoMapper : MapperBase<Metal, MetalListDto>
{
    [MapperIgnoreTarget(nameof(MetalListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(MetalListDto.FollowingUnitCode))]
    public override partial MetalListDto Map(Metal source);
    public override partial void Map(Metal source, MetalListDto destination);
}

// ── Future (Metal deseni: IsGlobal + FollowingUnitCode ignore) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FutureToGetDtoMapper : MapperBase<Future, FutureGetDto>
{
    [MapperIgnoreTarget(nameof(FutureGetDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(FutureGetDto.FollowingUnitCode))]
    public override partial FutureGetDto Map(Future source);
    public override partial void Map(Future source, FutureGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class FutureToListDtoMapper : MapperBase<Future, FutureListDto>
{
    [MapperIgnoreTarget(nameof(FutureListDto.IsGlobal))]
    [MapperIgnoreTarget(nameof(FutureListDto.FollowingUnitCode))]
    public override partial FutureListDto Map(Future source);
    public override partial void Map(Future source, FutureListDto destination);
}

// ── Jewelry / Stone (Service deseni: yalnız IsGlobal ignore; GroupCode entity'de) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class JewelryToGetDtoMapper : MapperBase<Jewelry, JewelryGetDto>
{
    [MapperIgnoreTarget(nameof(JewelryGetDto.IsGlobal))]
    public override partial JewelryGetDto Map(Jewelry source);
    public override partial void Map(Jewelry source, JewelryGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class JewelryToListDtoMapper : MapperBase<Jewelry, JewelryListDto>
{
    [MapperIgnoreTarget(nameof(JewelryListDto.IsGlobal))]
    public override partial JewelryListDto Map(Jewelry source);
    public override partial void Map(Jewelry source, JewelryListDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class StoneToGetDtoMapper : MapperBase<Stone, StoneGetDto>
{
    [MapperIgnoreTarget(nameof(StoneGetDto.IsGlobal))]
    public override partial StoneGetDto Map(Stone source);
    public override partial void Map(Stone source, StoneGetDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class StoneToListDtoMapper : MapperBase<Stone, StoneListDto>
{
    [MapperIgnoreTarget(nameof(StoneListDto.IsGlobal))]
    public override partial StoneListDto Map(Stone source);
    public override partial void Map(Stone source, StoneListDto destination);
}

// ── UserScopedGrant (statik ToDto → Mapperly; düz 1:1, enrichment yok) ──

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class UserScopedGrantToDtoMapper : MapperBase<UserScopedGrant, UserScopedGrantDto>
{
    public override partial UserScopedGrantDto Map(UserScopedGrant source);
    public override partial void Map(UserScopedGrant source, UserScopedGrantDto destination);
}

[Mapper] public partial class ServiceGetToCreateMapper : MapperBase<ServiceGetDto, ServiceCreateDto>
{
    public override partial ServiceCreateDto Map(ServiceGetDto source);
    public override partial void Map(ServiceGetDto source, ServiceCreateDto destination);
}
[Mapper] public partial class ServiceGetToUpdateMapper : MapperBase<ServiceGetDto, ServiceUpdateDto>
{
    public override partial ServiceUpdateDto Map(ServiceGetDto source);
    public override partial void Map(ServiceGetDto source, ServiceUpdateDto destination);
}

[Mapper] public partial class FutureGetToCreateMapper : MapperBase<FutureGetDto, FutureCreateDto>
{
    public override partial FutureCreateDto Map(FutureGetDto source);
    public override partial void Map(FutureGetDto source, FutureCreateDto destination);
}
[Mapper] public partial class FutureGetToUpdateMapper : MapperBase<FutureGetDto, FutureUpdateDto>
{
    public override partial FutureUpdateDto Map(FutureGetDto source);
    public override partial void Map(FutureGetDto source, FutureUpdateDto destination);
}

[Mapper] public partial class ScrapGetToCreateMapper : MapperBase<ScrapGetDto, ScrapCreateDto>
{
    public override partial ScrapCreateDto Map(ScrapGetDto source);
    public override partial void Map(ScrapGetDto source, ScrapCreateDto destination);
}
[Mapper] public partial class ScrapGetToUpdateMapper : MapperBase<ScrapGetDto, ScrapUpdateDto>
{
    public override partial ScrapUpdateDto Map(ScrapGetDto source);
    public override partial void Map(ScrapGetDto source, ScrapUpdateDto destination);
}

[Mapper] public partial class MetalGetToCreateMapper : MapperBase<MetalGetDto, MetalCreateDto>
{
    public override partial MetalCreateDto Map(MetalGetDto source);
    public override partial void Map(MetalGetDto source, MetalCreateDto destination);
}
[Mapper] public partial class MetalGetToUpdateMapper : MapperBase<MetalGetDto, MetalUpdateDto>
{
    public override partial MetalUpdateDto Map(MetalGetDto source);
    public override partial void Map(MetalGetDto source, MetalUpdateDto destination);
}

[Mapper] public partial class StoneGetToCreateMapper : MapperBase<StoneGetDto, StoneCreateDto>
{
    public override partial StoneCreateDto Map(StoneGetDto source);
    public override partial void Map(StoneGetDto source, StoneCreateDto destination);
}
[Mapper] public partial class StoneGetToUpdateMapper : MapperBase<StoneGetDto, StoneUpdateDto>
{
    public override partial StoneUpdateDto Map(StoneGetDto source);
    public override partial void Map(StoneGetDto source, StoneUpdateDto destination);
}

[Mapper] public partial class JewelryGetToCreateMapper : MapperBase<JewelryGetDto, JewelryCreateDto>
{
    public override partial JewelryCreateDto Map(JewelryGetDto source);
    public override partial void Map(JewelryGetDto source, JewelryCreateDto destination);
}
[Mapper] public partial class JewelryGetToUpdateMapper : MapperBase<JewelryGetDto, JewelryUpdateDto>
{
    public override partial JewelryUpdateDto Map(JewelryGetDto source);
    public override partial void Map(JewelryGetDto source, JewelryUpdateDto destination);
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

// SubAccount edit'i PersistentCoordinator üzerinden koşulsuz Map<GetDto,Create/UpdateDto> çağırır —
// bu mapper'lar yokken kaydet/güncelle runtime'da "No object mapping was found" fırlatıyordu (entegrasyon analizi E-3).
[Mapper] public partial class SubAccountGetToCreateMapper : MapperBase<SubAccountGetDto, SubAccountCreateDto>
{
    public override partial SubAccountCreateDto Map(SubAccountGetDto source);
    public override partial void Map(SubAccountGetDto source, SubAccountCreateDto destination);
}
[Mapper] public partial class SubAccountGetToUpdateMapper : MapperBase<SubAccountGetDto, SubAccountUpdateDto>
{
    public override partial SubAccountUpdateDto Map(SubAccountGetDto source);
    public override partial void Map(SubAccountGetDto source, SubAccountUpdateDto destination);
}
