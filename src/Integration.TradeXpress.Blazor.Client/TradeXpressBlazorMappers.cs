using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Currencies;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Currencies.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Countries.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Branches.Models;
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

// ── Branch (CompanyId güncellemede yok; CompanyName okuma-amaçlı) ────────────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchVmToCreateDtoMapper : MapperBase<BranchViewModel, BranchCreateDto>
{
    public override partial BranchCreateDto Map(BranchViewModel source);
    public override partial void Map(BranchViewModel source, BranchCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchVmToUpdateDtoMapper : MapperBase<BranchViewModel, BranchUpdateDto>
{
    public override partial BranchUpdateDto Map(BranchViewModel source);
    public override partial void Map(BranchViewModel source, BranchUpdateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class BranchGetDtoToVmMapper : MapperBase<BranchGetDto, BranchViewModel>
{
    public override partial BranchViewModel Map(BranchGetDto source);
    public override partial void Map(BranchGetDto source, BranchViewModel destination);
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
