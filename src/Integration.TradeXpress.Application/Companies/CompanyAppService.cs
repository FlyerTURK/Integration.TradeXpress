using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Countries;
using Integration.TradeXpress.Financials.CurrencyUnits;
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

namespace Integration.TradeXpress.Companies;

/// <summary>
/// Company CRUD — <b>per-tenant</b>. Sahip olduğu <b>şube grafını</b> (her şube de kendi kasa grafını)
/// tek komutta yönetir: standart Create/Update <see cref="CompanyGetDto.Branches"/>'i taşır, şube
/// yazımları <see cref="IBranchAppService"/>'e (o da kasaları <see cref="IVaultAppService"/>'e) DELEGE
/// edilir → Company→Branch→Vault recursive, tek UoW. Şube düğümü durumu Id + IsDeleted ile diff'lenir.
/// </summary>
[Authorize(TradeXpressPermissions.Companies.Default)]
public class CompanyAppService : TradeXpressAppService, ICompanyAppService
{
    private readonly IRepository<Company, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Country, Guid> _countryRepository; // yalnız OKUMA (CountryCode→Id çözümü, link için)
    private readonly IRepository<Branch, Guid> _branchRepository;   // yalnız OKUMA (graf projeksiyonu)
    private readonly IRepository<Vault, Guid> _vaultRepository;      // yalnız OKUMA
    private readonly IBranchAppService _branchAppService;            // YAZMA: şube create/update/delete buraya delege
    private readonly IDataFilter _dataFilter;
    private readonly OrgTreeManager _orgTree;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "CountryCode", "BaseCurrencyCode", "IsActive", "IsHeadquarters", "DisplayOrder", "Id" };

    public CompanyAppService(
        IRepository<Company, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Country, Guid> countryRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IBranchAppService branchAppService,
        IDataFilter dataFilter,
        OrgTreeManager orgTree)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _countryRepository = countryRepository;
        _branchRepository = branchRepository;
        _vaultRepository = vaultRepository;
        _branchAppService = branchAppService;
        _dataFilter = dataFilter;
        _orgTree = orgTree;
    }

    public virtual async Task<PagedResultDto<CompanyListDto>> GetListAsync(CompanyListRequestDto input)
    {
        // BaseCurrencyCode join'i GLOBAL birime (ör. TRY) eşleşir; tenant context'inde multi-tenant filtresi
        // global birimleri gizlediğinden INNER JOIN tüm şirketleri DÜŞÜRÜR. Filtreyi kapat + şirketleri
        // AÇIKÇA tenant'a göre kapsa (host'un şirketi yok → null). (CountryCode zaten Company'de gerçek kolon.)
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var units = await _unitRepository.GetQueryableAsync();
            var rows = (await _repository.GetQueryableAsync())
                .Where(c => c.TenantId == tenantId)
                .Join(units, c => c.BaseCurrencyUnitId, u => u.Id, (c, u) => new CompanyListRow
                {
                    Id = c.Id,
                    Code = c.Code,
                    Name = c.Name,
                    CountryCode = c.CountryCode,
                    BaseCurrencyUnitId = c.BaseCurrencyUnitId,
                    BaseCurrencyCode = u.Code,
                    IsActive = c.IsActive,
                    IsHeadquarters = c.IsHeadquarters,
                    DisplayOrder = c.DisplayOrder,
                })
                .ApplyListRequest(input, AllowedListFields);

            var totalCount = await AsyncExecuter.CountAsync(rows);
            var items = await AsyncExecuter.ToListAsync(rows.Skip(input.SkipCount).Take(input.MaxResultCount));

            // CountryCode bir string kolon (entity'de FK yok) → linklenebilmesi için Country.Id'yi Code'dan
            // çöz (bellekte; yalnız bu sayfanın kodları). Kapsam: global (TenantId=null) + mevcut tenant.
            var countryCodes = items.Select(r => r.CountryCode).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            var countryMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            if (countryCodes.Count > 0)
            {
                var countries = await _countryRepository.GetQueryableAsync();
                var matched = await AsyncExecuter.ToListAsync(
                    countries.Where(ct => countryCodes.Contains(ct.Code) && (ct.TenantId == null || ct.TenantId == tenantId)));
                foreach (var ct in matched)
                    countryMap[ct.Code] = ct.Id;   // code kapsam içinde tekil; çakışırsa son kazanır
            }

            return new PagedResultDto<CompanyListDto>(
                totalCount,
                items.Select(r => new CompanyListDto
                {
                    Id = r.Id,
                    Code = r.Code,
                    Name = r.Name,
                    CountryCode = r.CountryCode,
                    CountryId = countryMap.TryGetValue(r.CountryCode, out var cid) ? cid : (Guid?)null,
                    BaseCurrencyUnitId = r.BaseCurrencyUnitId,
                    BaseCurrencyCode = r.BaseCurrencyCode,
                    IsActive = r.IsActive,
                    IsHeadquarters = r.IsHeadquarters,
                    DisplayOrder = r.DisplayOrder,
                }).ToList());
        }
    }

    public virtual async Task<CompanyGetDto> GetAsync(Guid id)
    {
        var c = await _repository.GetAsync(id);
        return await ToGetDtoAsync(c);
    }

    [Authorize(TradeXpressPermissions.Companies.Create)]
    public virtual async Task<CompanyGetDto> CreateAsync(CompanyCreateDto input)
    {
        // Şirket/şube TENANT'a aittir — host (merkezi operasyon) şirket tanımlayamaz.
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        await EnsureCurrencyVisibleAsync(input.BaseCurrencyUnitId);

        var c = new Company(
            input.Code,
            input.Name,
            input.CountryCode,
            input.BaseCurrencyUnitId,
            isHeadquarters: input.IsHeadquarters,
            displayOrder: input.DisplayOrder);
        c.SetDescription(input.Description);
        await _repository.InsertAsync(c, autoSave: true);

        // Tek-HQ değişmezi: bu şirket HQ ise tenant'ın önceki HQ'sunu düşür.
        if (c.IsHeadquarters)
            await UnsetOtherHeadquartersAsync(c.Id);

        await SaveBranchesAsync(c, input.Branches);
        await _orgTree.EnsureHeadquartersBranchAsync(c);   // en az 1 HQ şube + varsayılan kasa (Branches boşsa da)

        return await ToGetDtoAsync(c);
    }

    [Authorize(TradeXpressPermissions.Companies.Update)]
    public virtual async Task<CompanyGetDto> UpdateAsync(Guid id, CompanyUpdateDto input)
    {
        await EnsureCurrencyVisibleAsync(input.BaseCurrencyUnitId);

        var c = await _repository.GetAsync(id);

        // HQ devri: HQ yap → tenant'ın diğer HQ'sunu düşür. Mevcut tek HQ'yu doğrudan düşürmek
        // yasak (önce başka bir şirkete devret); böylece tenant daima tam bir HQ şirkete sahiptir.
        if (input.IsHeadquarters && !c.IsHeadquarters)
        {
            c.SetAsHeadquarters(true);
            await UnsetOtherHeadquartersAsync(c.Id);
        }
        else if (!input.IsHeadquarters && c.IsHeadquarters)
        {
            throw new BusinessException("TradeXpress:Company:CannotUnsetHeadquarters");
        }

        c.SetCode(input.Code);
        c.SetName(input.Name);
        c.SetCountryCode(input.CountryCode);
        c.SetBaseCurrency(input.BaseCurrencyUnitId);
        c.SetDescription(input.Description);
        c.SetDisplayOrder(input.DisplayOrder);
        c.SetActive(input.IsActive);
        await _repository.UpdateAsync(c, autoSave: true);

        await SaveBranchesAsync(c, input.Branches);
        await _orgTree.EnsureHeadquartersBranchAsync(c);   // hiçbir koşulda şubesiz/HQ'suz kalmasın

        return await ToGetDtoAsync(c);
    }

    [Authorize(TradeXpressPermissions.Companies.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var c = await _repository.GetAsync(id);
        // HQ şirket, HQ başka bir şirkete devredilmedikçe silinemez (her zaman bir HQ kalır).
        if (c.IsHeadquarters)
            throw new BusinessException("TradeXpress:Company:CannotDeleteHeadquarters");

        // Çocukları (şube → kasa) cascade sil, sonra şirketi.
        await _orgTree.DeleteBranchesOfCompanyAsync(c.Id);
        await _repository.DeleteAsync(c, autoSave: true);
    }

    // ── şube grafı diff (Id + IsDeleted) → BranchAppService'e DELEGE ────────────
    // Company, şube/kasa repo'suna doğrudan yazmaz; her şube düğümünü BranchCreate/UpdateDto'ya (kasaları
    // dahil) map'leyip BranchAppService'e gönderir (o da kasaları VaultAppService'e). Tek UoW.
    private async Task SaveBranchesAsync(Company company, List<BranchGraphDto> branches)
    {
        // Silinmeyenler arasında TAM BİR HQ (forceOne) — "son HQ'yu devirsiz düşürme" hatasını önler.
        var live = branches.Where(b => !b.IsDeleted).ToList();
        NormalizeSingleFlag(live, b => b.IsHeadquarters, (b, v) => b.IsHeadquarters = v, forceOne: true);

        // Önce ekle + güncelle, HQ olanı İLK işle (yeni HQ DB'de eskiyi düşürsün → eskiyi false'a çekerken hata olmasın).
        foreach (var bi in live.OrderByDescending(b => b.IsHeadquarters))
        {
            if (bi.Id == Guid.Empty)
            {
                await _branchAppService.CreateAsync(new BranchCreateDto
                {
                    CompanyId = company.Id,
                    BaseCurrencyUnitId = bi.BaseCurrencyUnitId,   // null → BranchAppService şirketin base'ine düşer
                    Code = bi.Code,
                    Name = bi.Name,
                    IsHeadquarters = bi.IsHeadquarters,
                    DisplayOrder = bi.DisplayOrder,
                    Description = bi.Description,
                    Vaults = bi.Vaults,
                });
            }
            else
            {
                await _branchAppService.UpdateAsync(bi.Id, new BranchUpdateDto
                {
                    BaseCurrencyUnitId = bi.BaseCurrencyUnitId,   // şubenin kendi bilanço birimi (drill override)
                    Code = bi.Code,
                    Name = bi.Name,
                    IsHeadquarters = bi.IsHeadquarters,
                    IsActive = bi.IsActive,
                    DisplayOrder = bi.DisplayOrder,
                    Description = bi.Description,
                    Vaults = bi.Vaults,
                });
            }
        }

        // Sonra sil (yalnız mevcut + IsDeleted; yeni+silinen listeye hiç girmedi). HQ şube silinemez (BranchAppService guard).
        foreach (var bi in branches.Where(b => b.IsDeleted && b.Id != Guid.Empty))
        {
            await _branchAppService.DeleteAsync(bi.Id);
        }
    }

    // Tam bir bayrak: birden çoksa fazlalıkları, forceOne+hiç yoksa ilkini işaretle.
    private static void NormalizeSingleFlag<T>(List<T> items, Func<T, bool> get, Action<T, bool> set, bool forceOne)
    {
        var flagged = items.Where(get).ToList();
        if (flagged.Count > 1)
        {
            for (var i = 1; i < flagged.Count; i++)
                set(flagged[i], false);
        }
        else if (flagged.Count == 0 && forceOne && items.Count > 0)
        {
            set(items[0], true);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    /// <summary>Tenant başına tek HQ şirket: verilen hariç diğer HQ'ları düşürür.</summary>
    private async Task UnsetOtherHeadquartersAsync(Guid exceptCompanyId, bool autoSave = true)
    {
        var others = await AsyncExecuter.ToListAsync((await _repository.GetQueryableAsync())
            .Where(x => x.IsHeadquarters && x.Id != exceptCompanyId));
        foreach (var o in others)
        {
            o.SetAsHeadquarters(false);
            await _repository.UpdateAsync(o, autoSave: autoSave);
        }
    }

    /// <summary>Base currency görünür mü (global + own); değilse hata.</summary>
    private async Task EnsureCurrencyVisibleAsync(Guid unitId)
    {
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var tenantId = CurrentTenant.Id;
            var q = (await _unitRepository.GetQueryableAsync())
                .Where(u => u.Id == unitId && (u.TenantId == null || u.TenantId == tenantId));
            if (!await AsyncExecuter.AnyAsync(q))
                throw new EntityNotFoundException(typeof(CurrencyUnit), unitId);
        }
    }

    private async Task<Dictionary<Guid, string>> LoadCurrencyCodesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var q = (await _unitRepository.GetQueryableAsync()).Where(u => list.Contains(u.Id));
            var units = await AsyncExecuter.ToListAsync(q);
            return units.ToDictionary(u => u.Id, u => u.Code);
        }
    }

    // Şirket + şube + kasa grafını GetDto'ya doldurur (edit formu in-memory bind eder; ClientKey taze).
    private async Task<CompanyGetDto> ToGetDtoAsync(Company c)
    {
        var codes = await LoadCurrencyCodesAsync(new[] { c.BaseCurrencyUnitId });
        var branches = await AsyncExecuter.ToListAsync((await _branchRepository.GetQueryableAsync())
            .Where(b => b.CompanyId == c.Id).OrderBy(b => b.DisplayOrder));
        var branchIds = branches.Select(b => b.Id).ToList();
        var vaults = await AsyncExecuter.ToListAsync((await _vaultRepository.GetQueryableAsync())
            .Where(v => branchIds.Contains(v.BranchId)).OrderBy(v => v.DisplayOrder));

        return new CompanyGetDto
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name,
            CountryCode = c.CountryCode,
            BaseCurrencyUnitId = c.BaseCurrencyUnitId,
            BaseCurrencyCode = codes.GetValueOrDefault(c.BaseCurrencyUnitId, string.Empty),
            IsActive = c.IsActive,
            IsHeadquarters = c.IsHeadquarters,
            DisplayOrder = c.DisplayOrder,
            Description = c.Description,
            Branches = branches.Select(b => new BranchGraphDto
            {
                Id = b.Id,
                BaseCurrencyUnitId = b.BaseCurrencyUnitId,
                Code = b.Code,
                Name = b.Name,
                IsHeadquarters = b.IsHeadquarters,
                IsActive = b.IsActive,
                DisplayOrder = b.DisplayOrder,
                Description = b.Description,
                Vaults = vaults.Where(v => v.BranchId == b.Id).Select(v => new VaultGraphDto
                {
                    Id = v.Id,
                    Code = v.Code,
                    Name = v.Name,
                    IsDefault = v.IsDefault,
                    IsActive = v.IsActive,
                    DisplayOrder = v.DisplayOrder,
                    Description = v.Description,
                }).ToList(),
            }).ToList(),
        };
    }

    // Liste projeksiyonu: Company + join'lenmiş BaseCurrencyCode (gerçek string kolon → server-side sort/filter/arama).
    private sealed class CompanyListRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public Guid BaseCurrencyUnitId { get; set; }
        public string BaseCurrencyCode { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsHeadquarters { get; set; }
        public int DisplayOrder { get; set; }
    }
}
