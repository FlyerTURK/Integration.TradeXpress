using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Addressing;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Organization;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Branches;

/// <summary>
/// Branch (şube) standalone CRUD — <b>per-tenant</b>, sahip olduğu <b>kasaları (graf)</b> tek komutta yönetir.
/// Tek <see cref="BranchGraphDto"/> (get/create/update); kasa düğümü durumu Id + IsDeleted ile diff'lenir
/// (Id boş → ekle, IsDeleted → sil, aksi → güncelle). Değişmezler: şirket başına tek HQ; her şube en az
/// bir varsayılan kasa; HQ şube / şirketin son şubesi silinemez.
/// </summary>
[Authorize(TradeXpressPermissions.Branches.Default)]
public class BranchAppService : TradeXpressAppService, IBranchAppService
{
    private readonly IRepository<Branch, Guid> _repository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly IRepository<Country, Guid> _countryRepository;   // yalnız OKUMA (adres ülke-kilidi: Company.CountryId → Country.Code)
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;   // yalnız OKUMA (BaseCurrencyCode çözümü; global birim)
    private readonly IRepository<Vault, Guid> _vaultRepository;   // yalnız OKUMA (graf projeksiyonu)
    private readonly IVaultAppService _vaultAppService;            // YAZMA: kasa create/update/delete buraya delege
    private readonly IDataFilter _dataFilter;
    private readonly OrgTreeManager _orgTree;
    private readonly IScopedGrantResolver _scopedGrantResolver;   // working-context şube daraltması (yalnız OKUMA)
    private readonly ICurrentCompany _currentCompany;             // geçerli şirket picker scope'u (yalnız OKUMA)

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "CompanyCode", "BaseCurrencyCode", "IsHeadquarters", "IsActive", "DisplayOrder", "CompanyId", "Id" };

    public BranchAppService(
        IRepository<Branch, Guid> repository,
        IRepository<Company, Guid> companyRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Vault, Guid> vaultRepository,
        IVaultAppService vaultAppService,
        IDataFilter dataFilter,
        OrgTreeManager orgTree,
        IScopedGrantResolver scopedGrantResolver,
        ICurrentCompany currentCompany)
    {
        _repository = repository;
        _companyRepository = companyRepository;
        _countryRepository = countryRepository;
        _unitRepository = unitRepository;
        _vaultRepository = vaultRepository;
        _vaultAppService = vaultAppService;
        _dataFilter = dataFilter;
        _orgTree = orgTree;
        _scopedGrantResolver = scopedGrantResolver;
        _currentCompany = currentCompany;
    }

    public virtual async Task<PagedResultDto<BranchListDto>> GetListAsync(BranchListRequestDto input)
    {
        var rows = await BuildListRowQueryAsync();
        if (input.CompanyId.HasValue)
            rows = rows.Where(r => r.CompanyId == input.CompanyId.Value);
        rows = rows.ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(rows);
        var items = await AsyncExecuter.ToListAsync(rows.ApplyPaging(input));

        return new PagedResultDto<BranchListDto>(totalCount, await MapRowsToDtosAsync(items));
    }

    /// <summary>
    /// Working-context (sol menü şube seçici) için kullanıcının ERİŞEBİLDİĞİ şubeler — server-side kapsam
    /// (scope) daraltması: <see cref="IScopedGrantResolver"/> ile çözülen erişim kümesi her satırı
    /// <c>CanAccessBranch</c> ile eler (en-spesifik-kazanır; company-grant tüm şubelerini, company-deny +
    /// branch-grant yalnız o şubeyi açar). Client'a ASLA güvenilmez: combo daraltması burada, sunucuda olur.
    /// Kendi çalışma kapsamının okunması → yalnız kimliklendirilmiş kullanıcı yeter (Branches.Default gerekmez).
    /// </summary>
    [Authorize]
    public virtual async Task<List<BranchListDto>> GetMyBranchesAsync()
    {
        var access = await _scopedGrantResolver.ResolveAsync(CurrentUser.GetId());

        var rows = await BuildListRowQueryAsync();
        var allowed = (await AsyncExecuter.ToListAsync(rows))
            .Where(r => access.CanAccessBranch(r.CompanyId, r.Id))
            .ToList();

        return await MapRowsToDtosAsync(allowed);
    }

    /// <summary>GEÇERLİ çalışılan şirketin AKTİF şubeleri (picker/lookup) — sunucu <see cref="ICurrentCompany"/> ile
    /// scope'lar (client CompanyId göndermez). Şirket bağlamı yoksa boş. Yalnız kimliklendirilmiş kullanıcı yeter
    /// (company-owned katalog seçimi; Branches.Default gerekmez). ShipmentTemplate.GetPickerListAsync deseni.</summary>
    [Authorize]
    public virtual async Task<List<BranchListDto>> GetMyCompanyBranchesAsync()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            return new List<BranchListDto>();
        }

        var rows = await BuildListRowQueryAsync();
        var items = await AsyncExecuter.ToListAsync(
            rows.Where(r => r.CompanyId == companyId && r.IsActive)
                .OrderBy(r => r.DisplayOrder)
                .ThenBy(r => r.Name));

        var dtos = await MapRowsToDtosAsync(items);
        await FillAddressesAsync(dtos);   // picker path: şube adresini de doldur (kargo şablonu şube modu özeti/düzenlemesi)
        return dtos;
    }

    /// <summary>Picker DTO'larına şube adresini doldurur (owned <see cref="Branch.Address"/> aggregate ile yüklenir;
    /// grid listesi bunu ÇAĞIRMAZ → yalnız picker gereksiz kolon taşımaz). Elle Address→DTO okuma (<see cref="ToAddressDto"/>).</summary>
    private async Task FillAddressesAsync(List<BranchListDto> dtos)
    {
        if (dtos.Count == 0)
        {
            return;
        }

        var ids = dtos.Select(d => d.Id).ToList();
        var entities = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync()).Where(b => ids.Contains(b.Id)));
        var addressMap = entities.ToDictionary(b => b.Id, b => ToAddressDto(b.Address));

        foreach (var d in dtos)
        {
            d.Address = addressMap.GetValueOrDefault(d.Id);
        }
    }

    // CompanyCode enrichment'tı → join ile GERÇEK kolon yap: kod ile sort/filter/arama server-side çalışsın.
    private async Task<IQueryable<BranchListRow>> BuildListRowQueryAsync()
    {
        var companies = await _companyRepository.GetQueryableAsync();
        var query = await _repository.GetQueryableAsync();
        return query
            .Join(companies, b => b.CompanyId, c => c.Id, (b, c) => new BranchListRow
            {
                Id = b.Id,
                CompanyId = b.CompanyId,
                CompanyCode = c.Code,
                CompanyName = c.Name,
                CompanyCountryId = c.CountryId,   // adres ülke varsayılanı (şube picker → gönderim özel adres ülkesi)
                BaseCurrencyUnitId = b.BaseCurrencyUnitId,
                Code = b.Code,
                Name = b.Name,
                IsHeadquarters = b.IsHeadquarters,
                IsActive = b.IsActive,
                DisplayOrder = b.DisplayOrder,
            });
    }

    private async Task<List<BranchListDto>> MapRowsToDtosAsync(List<BranchListRow> items)
    {
        // BaseCurrencyCode: birim GLOBAL (TenantId=null) → tenant filtresi join'i düşürür; filtreyi kapatıp
        // yalnız bu sayfanın birim id'lerini bellekte koda çöz (Company.GetListAsync ile aynı yaklaşım).
        var unitIds = items.Select(r => r.BaseCurrencyUnitId).Where(id => id != Guid.Empty).Distinct().ToList();
        var unitMap = new Dictionary<Guid, string>();
        if (unitIds.Count > 0)
        {
            using (_dataFilter.Disable<IMultiTenant>())
            {
                var units = await _unitRepository.GetQueryableAsync();
                var matched = await AsyncExecuter.ToListAsync(units.Where(u => unitIds.Contains(u.Id)));
                foreach (var u in matched)
                    unitMap[u.Id] = u.Code;
            }
        }

        return items.Select(r => new BranchListDto
        {
            Id = r.Id,
            CompanyId = r.CompanyId,
            CompanyCode = r.CompanyCode,
            CompanyName = r.CompanyName,
            CompanyCountryId = r.CompanyCountryId,
            BaseCurrencyUnitId = r.BaseCurrencyUnitId,
            BaseCurrencyCode = unitMap.GetValueOrDefault(r.BaseCurrencyUnitId, string.Empty),
            Code = r.Code,
            Name = r.Name,
            IsHeadquarters = r.IsHeadquarters,
            IsActive = r.IsActive,
            DisplayOrder = r.DisplayOrder,
        }).ToList();
    }

    public virtual async Task<BranchGetDto> GetAsync(Guid id)
    {
        var b = await _repository.GetAsync(id);
        return await ToGetDtoAsync(b);
    }

    [Authorize(TradeXpressPermissions.Branches.Create)]
    public virtual async Task<BranchGetDto> CreateAsync(BranchCreateDto input)
    {
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        var company = await _companyRepository.FindAsync(input.CompanyId)
            ?? throw new EntityNotFoundException(typeof(Company), input.CompanyId);

        // Benzersizlik ÖN-kontrolü (şirket scope): aynı şirkette aynı kodlu şube → dostane hata (Update'le simetrik).
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Branch.Code), EntityFieldConsts.CodeMinLength, BranchConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(input.CompanyId, normalizedCode, Guid.Empty);

        var b = new Branch(
            input.CompanyId,
            input.Code,
            input.Name,
            isHeadquarters: input.IsHeadquarters,
            displayOrder: input.DisplayOrder);
        b.SetDescription(input.Description);
        b.SetBaseCurrency(input.BaseCurrencyUnitId == Guid.Empty ? company.BaseCurrencyUnitId : input.BaseCurrencyUnitId);   // boş → parent şirketin base'i
        b.SetAddress(await BuildAddressAsync(input.Address, company));   // ülke-kilitli (şube adresi şirketin ülkesinde)
        await _repository.InsertAsync(b, autoSave: true);

        // Tek-HQ değişmezi: bu şube HQ ise şirketin önceki HQ'sunu düşür.
        if (b.IsHeadquarters)
            await UnsetOtherHeadquartersAsync(b.CompanyId, b.Id);

        await SaveVaultsAsync(b, input.Vaults);
        await _orgTree.EnsureDefaultVaultAsync(b);   // en az 1 varsayılan kasa (Vaults boşsa da)

        return await ToGetDtoAsync(b);
    }

    [Authorize(TradeXpressPermissions.Branches.Update)]
    public virtual async Task<BranchGetDto> UpdateAsync(Guid id, BranchUpdateDto input)
    {
        var b = await _repository.GetAsync(id);

        // HQ devri: HQ yap → diğerlerini düşür. Mevcut tek HQ'yu doğrudan düşürmek yasak (önce devret).
        if (input.IsHeadquarters && !b.IsHeadquarters)
        {
            b.SetAsHeadquarters(true);
            await UnsetOtherHeadquartersAsync(b.CompanyId, b.Id);
        }
        else if (!input.IsHeadquarters && b.IsHeadquarters)
        {
            throw new BusinessException("TradeXpress:Branch:CannotUnsetHeadquarters");
        }

        await ApplyCodeChangeAsync(b, input.Code);
        b.SetName(input.Name);
        b.SetDescription(input.Description);
        b.SetDisplayOrder(input.DisplayOrder);
        b.SetBaseCurrency(input.BaseCurrencyUnitId == Guid.Empty ? b.BaseCurrencyUnitId : input.BaseCurrencyUnitId);   // boş gelirse mevcut değeri KORU (wipe önleme)
        b.SetActive(input.IsActive);

        // Adres: standalone form kendi adresini gönderir, Company-grafı yolu mevcut adresi faithful round-trip eder
        // (null/boş → temizle; dolu → şirketin ülkesine kilitle). Parent şirket ülke-kilidi için çözülür.
        var company = await _companyRepository.GetAsync(b.CompanyId);
        b.SetAddress(await BuildAddressAsync(input.Address, company));
        await _repository.UpdateAsync(b, autoSave: true);

        await SaveVaultsAsync(b, input.Vaults);
        await _orgTree.EnsureDefaultVaultAsync(b);   // hiçbir koşulda kasasız kalmasın

        return await ToGetDtoAsync(b);
    }

    [Authorize(TradeXpressPermissions.Branches.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var b = await _repository.GetAsync(id);

        if (b.IsHeadquarters)
            throw new BusinessException("TradeXpress:Branch:CannotDeleteHeadquarters");

        var siblingCount = await AsyncExecuter.CountAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.CompanyId == b.CompanyId));
        if (siblingCount <= 1)
            throw new BusinessException("TradeXpress:Branch:CompanyMustHaveBranch");

        await _orgTree.DeleteVaultsOfBranchAsync(b.Id);
        await _repository.DeleteAsync(b, autoSave: true);
    }

    /// <summary>Şube adresini GÜNCELLER (kargo şablonu ŞUBE modunda inline düzenleme; cross-entity yazma). Yalnız
    /// adres alanına dokunur (Code/Name/Vaults ELLENMEZ). Ülke şirketin ülkesine KİLİTLİ (<see cref="BuildAddressAsync"/>).
    /// Company-scope guard: şube GEÇERLİ çalışılan şirkete ait olmalı — cross-company yazma reddedilir. Branches.Update
    /// yetkisi gerektirir (adres yazımı = şube güncellemesi; güvenlik gevşetilmez).</summary>
    [Authorize(TradeXpressPermissions.Branches.Update)]
    public virtual async Task<BranchAddressDto?> UpdateAddressAsync(Guid branchId, BranchAddressDto address)
    {
        var b = await _repository.GetAsync(branchId);

        // Company-scope guard: yalnız geçerli çalışılan şirketin şubesi düzenlenebilir (cross-company sızıntı önlenir).
        if (_currentCompany.Id is not { } companyId || b.CompanyId != companyId)
        {
            throw new BusinessException("TradeXpress:Branch:NotInCurrentCompany");
        }

        var company = await _companyRepository.GetAsync(b.CompanyId);
        b.SetAddress(await BuildAddressAsync(address, company));   // ülke şirketin ülkesine kilitli (mevcut 1b mantığı)
        await _repository.UpdateAsync(b, autoSave: true);

        return ToAddressDto(b.Address);
    }

    // ── kasa grafı diff (Id + IsDeleted) → VaultAppService'e DELEGE ─────────────
    // Branch, kasa repo'suna doğrudan yazmaz; her düğümü VaultCreate/UpdateDto'ya map'leyip
    // VaultAppService'e gönderir (sahiplik VaultAppService'te; kendi değişmezlerini o uygular).
    private async Task SaveVaultsAsync(Branch branch, List<VaultGraphDto> vaults)
    {
        NormalizeSingleDefault(vaults);   // silinmeyenler arasında tam bir varsayılan

        // Önce ekle + güncelle (silmeden ÖNCE → "şubede en az 1 kasa" guard'ı tetiklenmesin).
        foreach (var vi in vaults.Where(v => !v.IsDeleted))
        {
            if (vi.Id == Guid.Empty)
            {
                await _vaultAppService.CreateAsync(new VaultCreateDto
                {
                    BranchId = branch.Id,
                    Code = vi.Code,
                    Name = vi.Name,
                    IsDefault = vi.IsDefault,
                    DisplayOrder = vi.DisplayOrder,
                    Description = vi.Description,
                });
            }
            else
            {
                await _vaultAppService.UpdateAsync(vi.Id, new VaultUpdateDto
                {
                    Code = vi.Code,
                    Name = vi.Name,
                    IsDefault = vi.IsDefault,
                    IsActive = vi.IsActive,
                    DisplayOrder = vi.DisplayOrder,
                    Description = vi.Description,
                });
            }
        }

        // Sonra sil (yalnız mevcut + IsDeleted; yeni+silinen listeye hiç girmedi).
        foreach (var vi in vaults.Where(v => v.IsDeleted && v.Id != Guid.Empty))
        {
            await _vaultAppService.DeleteAsync(vi.Id);
        }
    }

    // Silinmeyen kasalar arasında TAM BİR varsayılan: hiç yoksa ilkini, birden çoksa fazlalıkları düşür.
    private static void NormalizeSingleDefault(List<VaultGraphDto> vaults)
    {
        var live = vaults.Where(v => !v.IsDeleted).OrderBy(v => v.DisplayOrder).ToList();
        if (live.Count == 0)
            return;
        if (!live.Any(v => v.IsDefault))
            live[0].IsDefault = true;

        var seen = false;
        foreach (var v in live)
        {
            if (!v.IsDefault)
                continue;
            if (seen)
                v.IsDefault = false;
            else
                seen = true;
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Kod değişikliği (kod düzenlenebilir ürün kuralı): normalize → değiştiyse AYNI ŞİRKET altında
    /// benzersizliği doğrula (kendisi hariç; dostane hata) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Branch b, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(b.Code), EntityFieldConsts.CodeMinLength, BranchConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, b.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(b.CompanyId, normalizedCode, b.Id);
        b.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞİRKET altında Code benzersizliği. Create'te <paramref name="excludeId"/>=Guid.Empty,
    /// Update'te b.Id. Dostane BusinessException — ham DB unique çakışmasını önler.</summary>
    private async Task EnsureCodeUniqueAsync(Guid companyId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.CompanyId == companyId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Branch:CodeAlreadyExists");
        }
    }

    private async Task UnsetOtherHeadquartersAsync(Guid companyId, Guid exceptBranchId)
    {
        var others = await AsyncExecuter.ToListAsync((await _repository.GetQueryableAsync())
            .Where(x => x.CompanyId == companyId && x.IsHeadquarters && x.Id != exceptBranchId));
        foreach (var o in others)
        {
            o.SetAsHeadquarters(false);
            await _repository.UpdateAsync(o, autoSave: true);
        }
    }

    /// <summary>Flat adres DTO'sunu ülke-KİLİTLİ <see cref="Address"/> VO'ya çevirir (ShipmentTemplate.ToAddress
    /// deseni). Kurallar: DTO null VEYA City+Line boş → <c>null</c> (adres yok — boş VO ctor'u throw eder, o yüzden
    /// AppService boş→null indirger). Dolu → CountryCode şubenin <paramref name="company"/> ülkesine ZORLANIR
    /// (branch adresi company ülkesinde olmalı — kullanıcı kuralı). Company.CountryId null (legacy) ise verilen/default korunur.</summary>
    private async Task<Address?> BuildAddressAsync(BranchAddressDto? dto, Company company)
    {
        // ADRES ZORUNLU (kullanıcı kararı 2026-07-25): HER şubenin adresi olmalı — adressiz şube kargo/fatura
        // akışlarında sessizce eksik veri üretiyordu. Eskiden boş adres "adres yok" sayılıp null'a indirgeniyordu.
        // Bu kapı TÜM yolları kapsar: Create, Update ve inline UpdateAddressAsync (adres TEMİZLENEMEZ de);
        // şirket grafı da şubeleri BranchAppService'e delege ettiğinden oradan da geçer.
        if (dto is null || (string.IsNullOrWhiteSpace(dto.City) && string.IsNullOrWhiteSpace(dto.Line)))
        {
            throw new BusinessException("TradeXpress:Branch:AddressRequired");
        }

        var countryCode = await ResolveCompanyCountryCodeAsync(company) ?? dto.CountryCode;
        return new Address(
            dto.City,
            dto.Line,
            dto.District,
            dto.Neighborhood,
            dto.PostalCode,
            countryCode,
            dto.Title,
            dto.CityCode,
            dto.DistrictCode,
            dto.AdministrativeAreaId,
            dto.LocalityId,
            dto.AdministrativeAreaIsoCode,
            buildingName: dto.BuildingName,
            buildingNumber: dto.BuildingNumber,
            room: dto.Room,
            floor: dto.Floor,
            postbox: dto.Postbox,
            additionalStreetName: dto.AdditionalStreetName);
    }

    /// <summary>Şirketin ülke kodunu çözer (Company.CountryId → Country.Code). Country host‖tenant kataloğu →
    /// global kaydı görebilmek için IMultiTenant filtresi kapatılır (CompanyAppService.LoadCountryCodeAsync deseni).
    /// CountryId null (legacy) → null (kilit yok, çağıran verilen/default'a düşer).</summary>
    private async Task<string?> ResolveCompanyCountryCodeAsync(Company company)
    {
        if (company.CountryId is not { } countryId)
        {
            return null;
        }

        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await AsyncExecuter.FirstOrDefaultAsync(
                (await _countryRepository.GetQueryableAsync())
                    .Where(c => c.Id == countryId)
                    .Select(c => c.Code));
        }
    }

    /// <summary>Address VO → flat DTO (hand-read; Address→DTO elle okuma — CompanyAppService ile aynı desen).
    /// null VO → null DTO (adres yok).</summary>
    private static BranchAddressDto? ToAddressDto(Address? a)
    {
        if (a is null)
        {
            return null;
        }

        return new BranchAddressDto
        {
            Title = a.Title,
            City = a.City,
            Line = a.Line,
            District = a.District,
            Neighborhood = a.Neighborhood,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode,
            CityCode = a.CityCode,
            DistrictCode = a.DistrictCode,
            AdministrativeAreaId = a.AdministrativeAreaId,
            LocalityId = a.LocalityId,
            AdministrativeAreaIsoCode = a.AdministrativeAreaIsoCode,
            BuildingName = a.BuildingName,
            BuildingNumber = a.BuildingNumber,
            Room = a.Room,
            Floor = a.Floor,
            Postbox = a.Postbox,
            AdditionalStreetName = a.AdditionalStreetName,
        };
    }

    private async Task<BranchGetDto> ToGetDtoAsync(Branch b)
    {
        // Parent şirket: CompanyCode (görüntü) + CountryId (adres formunun ülke kilidi = FixedCountryId).
        var company = await _companyRepository.FindAsync(b.CompanyId);
        var vaults = await AsyncExecuter.ToListAsync(
            (await _vaultRepository.GetQueryableAsync()).Where(v => v.BranchId == b.Id).OrderBy(v => v.DisplayOrder));

        return new BranchGetDto
        {
            Id = b.Id,
            CompanyId = b.CompanyId,
            CompanyCode = company?.Code ?? string.Empty,
            CompanyCountryId = company?.CountryId,
            BaseCurrencyUnitId = b.BaseCurrencyUnitId,
            Code = b.Code,
            Name = b.Name,
            IsHeadquarters = b.IsHeadquarters,
            IsActive = b.IsActive,
            DisplayOrder = b.DisplayOrder,
            Description = b.Description,
            Address = ToAddressDto(b.Address),
            Vaults = vaults.Select(v => new VaultGraphDto
            {
                Id = v.Id,
                Code = v.Code,
                Name = v.Name,
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
                Description = v.Description,
            }).ToList(),
        };
    }

    // Liste projeksiyonu: Branch + join'lenmiş CompanyCode (gerçek string kolon → server-side sort/filter/arama).
    private sealed class BranchListRow
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public Guid? CompanyCountryId { get; set; }
        public Guid BaseCurrencyUnitId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsHeadquarters { get; set; }
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }
}
