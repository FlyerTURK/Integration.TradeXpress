using Riok.Mapperly.Abstractions;
using Volo.Abp.Mapperly;
using Integration.TradeXpress.Tenants;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.Parities;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Blazor.Client.Pages.Countries.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults.Models;
using Integration.TradeXpress.Blazor.Client.Pages.Admin.Models;
using Integration.TradeXpress.Blazor.Client.Services;

namespace Integration.TradeXpress.Blazor.Client;

// TenantViewModel mapper'ları kaldırıldı — tenant edit formu artık GetDto'ya doğrudan bind ediliyor
// (in-memory Users/Companies DrillList'leri). ViewModel legacy.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToTenantListDtoMapper : MapperBase<TenantGetDto, TenantListDto>
{
    public override partial TenantListDto Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantListDto destination);
}

// ── CurrencyUnit (tüm alanlar düz; ad eşleşmesiyle map'lenir) ───────────────────
// ViewModel mapper'ları kaldırıldı — edit formu GetDto'ya doğrudan bind ediliyor.

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class CurrencyUnitGetDtoToListDtoMapper : MapperBase<CurrencyUnitGetDto, CurrencyUnitListDto>
{
    public override partial CurrencyUnitListDto Map(CurrencyUnitGetDto source);
    public override partial void Map(CurrencyUnitGetDto source, CurrencyUnitListDto destination);
}

// CurrencyUnitMargin: append-only + custom AppService (ICrud değil) → ViewModel/CRUD mapper YOK.
// Sayfa doğrudan ICurrencyUnitMarginAppService (GetList/Set/History) kullanır.

// ── Company (düz alanlar) ───────────────────────────────────────────────────────
// ViewModel mapper'ları kaldırıldı — edit formu GetDto'ya doğrudan bind ediliyor.

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
//    fazla alanlar (Id, *Code görünüm alanları, IsGlobal) yok sayılır. ────────

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

// Parity: GetDto'daki enrichment/computed alanlar (BaseCode/QuoteCode/IsSystem/IsGlobal) Target
// strategy ile yok sayılır. Update yalnız IsActive/DisplayOrder (base/quote değişmez).
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityGetDtoToCreateDtoMapper : MapperBase<ParityGetDto, ParityCreateDto>
{
    public override partial ParityCreateDto Map(ParityGetDto source);
    public override partial void Map(ParityGetDto source, ParityCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class ParityGetDtoToUpdateDtoMapper : MapperBase<ParityGetDto, ParityUpdateDto>
{
    public override partial ParityUpdateDto Map(ParityGetDto source);
    public override partial void Map(ParityGetDto source, ParityUpdateDto destination);
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

// Tenant: Name/AdminEmail/AdminPassword + HqCompanyName/HqCountryCode (onboarding) GetDto'da var → create'e map'lenir.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToCreateDtoMapper : MapperBase<TenantGetDto, TenantCreateDto>
{
    public override partial TenantCreateDto Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantCreateDto destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class TenantGetDtoToUpdateDtoMapper : MapperBase<TenantGetDto, TenantUpdateDto>
{
    public override partial TenantUpdateDto Map(TenantGetDto source);
    public override partial void Map(TenantGetDto source, TenantUpdateDto destination);
}

// ── Identity (Role/User) — GetDto → Create/Update (CrudEditComponentBase.SaveAsync ObjectMapper ile
//    GetDto→Create/Update map'ler, sonra adapter ABP IIdentity*AppService'e gönderir). Target strategy:
//    GetDto'daki fazlalar (Id, IsStatic [create], Password [update]) yok sayılır. ─────────

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class RoleGetDtoToCreateInputMapper : MapperBase<RoleGetDto, CreateIdentityRoleInput>
{
    public override partial CreateIdentityRoleInput Map(RoleGetDto source);
    public override partial void Map(RoleGetDto source, CreateIdentityRoleInput destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class RoleGetDtoToUpdateInputMapper : MapperBase<RoleGetDto, UpdateIdentityRoleInput>
{
    public override partial UpdateIdentityRoleInput Map(RoleGetDto source);
    public override partial void Map(RoleGetDto source, UpdateIdentityRoleInput destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class UserGetDtoToCreateInputMapper : MapperBase<UserGetDto, CreateIdentityUserInput>
{
    public override partial CreateIdentityUserInput Map(UserGetDto source);
    public override partial void Map(UserGetDto source, CreateIdentityUserInput destination);
}

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public partial class UserGetDtoToUpdateInputMapper : MapperBase<UserGetDto, UpdateIdentityUserInput>
{
    public override partial UpdateIdentityUserInput Map(UserGetDto source);
    public override partial void Map(UserGetDto source, UpdateIdentityUserInput destination);
}
