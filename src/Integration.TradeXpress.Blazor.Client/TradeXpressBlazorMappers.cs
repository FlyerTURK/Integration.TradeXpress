using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Financials.CurrencyUnits.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Countries.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults.Models;

namespace Integration.TradeXpress.Blazor.Client;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantViewModelToTenantCreateDtoMapper : MapperBase<TenantViewModel, TenantCreateDto>
{
    public override partial TenantCreateDto Map(TenantViewModel source);
    public override partial void Map(TenantViewModel source, TenantCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantViewModelToTenantUpdateDtoMapper : MapperBase<TenantViewModel, TenantUpdateDto>
{
    public override partial TenantUpdateDto Map(TenantViewModel source);
    public override partial void Map(TenantViewModel source, TenantUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToTenantViewModelMapper : MapperBase<TenantGetDto, TenantViewModel>
{
    [MapperIgnoreTarget(nameof(TenantViewModel.AdminEmailAddress))]
    [MapperIgnoreTarget(nameof(TenantViewModel.AdminPassword))]
    public override partial TenantViewModel Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantViewModel destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToTenantListDtoMapper : MapperBase<TenantGetDto, TenantListDto>
{
    public override partial TenantListDto Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantListDto destination);
}

// ── CurrencyUnit (tüm alanlar düz; ad eşleşmesiyle map'lenir) ───────────────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitViewModelToCreateDtoMapper : MapperBase<CurrencyUnitViewModel, CurrencyUnitCreateDto>
{
    public override partial CurrencyUnitCreateDto Map(CurrencyUnitViewModel source);
    public override partial void Map(CurrencyUnitViewModel source, CurrencyUnitCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitViewModelToUpdateDtoMapper : MapperBase<CurrencyUnitViewModel, CurrencyUnitUpdateDto>
{
    public override partial CurrencyUnitUpdateDto Map(CurrencyUnitViewModel source);
    public override partial void Map(CurrencyUnitViewModel source, CurrencyUnitUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitGetDtoToViewModelMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitViewModel>
{
    public override partial CurrencyUnitViewModel Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitViewModel destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitGetDtoToListDtoMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitListDto>
{
    public override partial CurrencyUnitListDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitListDto destination);
}

// CurrencyUnitMargin: append-only + custom AppService (ICrud değil) → ViewModel/CRUD mapper YOK.
// Sayfa doğrudan ICurrencyUnitMarginAppService (GetList/Set/History) kullanır.

// ── Company (düz alanlar) ───────────────────────────────────────────────────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyVmToCreateDtoMapper : MapperBase<CompanyViewModel, CompanyCreateDto>
{
    public override partial CompanyCreateDto Map(CompanyViewModel source);
    public override partial void Map(CompanyViewModel source, CompanyCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyVmToUpdateDtoMapper : MapperBase<CompanyViewModel, CompanyUpdateDto>
{
    public override partial CompanyUpdateDto Map(CompanyViewModel source);
    public override partial void Map(CompanyViewModel source, CompanyUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyGetDtoToVmMapper : MapperBase<CompanyGetDto, CompanyViewModel>
{
    public override partial CompanyViewModel Map(CompanyGetDto source);
    public override partial void Map(CompanyGetDto source, CompanyViewModel destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyGetDtoToListDtoMapper : MapperBase<CompanyGetDto, CompanyListDto>
{
    public override partial CompanyListDto Map(CompanyGetDto source);
    public override partial void Map(CompanyGetDto source, CompanyListDto destination);
}

// ── Country (düz alanlar) ───────────────────────────────────────────────────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryVmToCreateDtoMapper : MapperBase<CountryViewModel, CountryCreateDto>
{
    public override partial CountryCreateDto Map(CountryViewModel source);
    public override partial void Map(CountryViewModel source, CountryCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryVmToUpdateDtoMapper : MapperBase<CountryViewModel, CountryUpdateDto>
{
    public override partial CountryUpdateDto Map(CountryViewModel source);
    public override partial void Map(CountryViewModel source, CountryUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryGetDtoToVmMapper : MapperBase<CountryGetDto, CountryViewModel>
{
    public override partial CountryViewModel Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryViewModel destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryGetDtoToListDtoMapper : MapperBase<CountryGetDto, CountryListDto>
{
    public override partial CountryListDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryListDto destination);
}


[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchGetDtoToListDtoMapper : MapperBase<BranchGetDto, BranchListDto>
{
    public override partial BranchListDto Map(BranchGetDto source);
    public override partial void Map(BranchGetDto source, BranchListDto destination);
}

// ── Vault (BranchId güncellemede yok; BranchName okuma-amaçlı) ───────────────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultVmToCreateDtoMapper : MapperBase<VaultViewModel, VaultCreateDto>
{
    public override partial VaultCreateDto Map(VaultViewModel source);
    public override partial void Map(VaultViewModel source, VaultCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultVmToUpdateDtoMapper : MapperBase<VaultViewModel, VaultUpdateDto>
{
    public override partial VaultUpdateDto Map(VaultViewModel source);
    public override partial void Map(VaultViewModel source, VaultUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultGetDtoToVmMapper : MapperBase<VaultGetDto, VaultViewModel>
{
    public override partial VaultViewModel Map(VaultGetDto source);
    public override partial void Map(VaultGetDto source, VaultViewModel destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultGetDtoToListDtoMapper : MapperBase<VaultGetDto, VaultListDto>
{
    public override partial VaultListDto Map(VaultGetDto source);
    public override partial void Map(VaultGetDto source, VaultListDto destination);
}

// ── GetDto → Create/Update (CrudEditComponentBase doğrudan GetDto bind eder; SaveAsync
//    ObjectMapper.Map<GetDto, Create/UpdateDto> çağırır). Bu mapper'lar olmadan kaydetme
//    "No object mapping was found" atar. RequiredMappingStrategy.Target → GetDto'daki
//    fazla alanlar (Id, *Code görünüm alanları, IsGlobal, PageIndex) yok sayılır. ────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitGetDtoToCreateDtoMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitCreateDto>
{
    public override partial CurrencyUnitCreateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitGetDtoToUpdateDtoMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitUpdateDto>
{
    public override partial CurrencyUnitUpdateDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyGetDtoToCreateDtoMapper : MapperBase<CompanyGetDto, CompanyCreateDto>
{
    public override partial CompanyCreateDto Map(CompanyGetDto source);
    public override partial void Map(CompanyGetDto source, CompanyCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CompanyGetDtoToUpdateDtoMapper : MapperBase<CompanyGetDto, CompanyUpdateDto>
{
    public override partial CompanyUpdateDto Map(CompanyGetDto source);
    public override partial void Map(CompanyGetDto source, CompanyUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryGetDtoToCreateDtoMapper : MapperBase<CountryGetDto, CountryCreateDto>
{
    public override partial CountryCreateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CountryGetDtoToUpdateDtoMapper : MapperBase<CountryGetDto, CountryUpdateDto>
{
    public override partial CountryUpdateDto Map(CountryGetDto source);
    public override partial void Map(CountryGetDto source, CountryUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchGetDtoToCreateDtoMapper : MapperBase<BranchGetDto, BranchCreateDto>
{
    public override partial BranchCreateDto Map(BranchGetDto source);
    public override partial void Map(BranchGetDto source, BranchCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchGetDtoToUpdateDtoMapper : MapperBase<BranchGetDto, BranchUpdateDto>
{
    public override partial BranchUpdateDto Map(BranchGetDto source);
    public override partial void Map(BranchGetDto source, BranchUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultGetDtoToCreateDtoMapper : MapperBase<VaultGetDto, VaultCreateDto>
{
    public override partial VaultCreateDto Map(VaultGetDto source);
    public override partial void Map(VaultGetDto source, VaultCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class VaultGetDtoToUpdateDtoMapper : MapperBase<VaultGetDto, VaultUpdateDto>
{
    public override partial VaultUpdateDto Map(VaultGetDto source);
    public override partial void Map(VaultGetDto source, VaultUpdateDto destination);
}

// Tenant: admin alanları GetDto'da var (Name/AdminEmail/AdminPassword) → create'e map'lenir;
// HqCompanyName/HqCountryCode GetDto'da YOK → ignore (tenant HQ şirketi opsiyonel).
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToCreateDtoMapper : MapperBase<TenantGetDto, TenantCreateDto>
{
    [MapperIgnoreTarget(nameof(TenantCreateDto.HqCompanyName))]
    [MapperIgnoreTarget(nameof(TenantCreateDto.HqCountryCode))]
    public override partial TenantCreateDto Map(TenantGetDto source);
    [MapperIgnoreTarget(nameof(TenantCreateDto.HqCompanyName))]
    [MapperIgnoreTarget(nameof(TenantCreateDto.HqCountryCode))]
    public override partial void Map(TenantGetDto source, TenantCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToUpdateDtoMapper : MapperBase<TenantGetDto, TenantUpdateDto>
{
    public override partial TenantUpdateDto Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantUpdateDto destination);
}
