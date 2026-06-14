using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Currencies;
using Integration.TradeXpress.Organization;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Companies;

/// <summary>
/// Company CRUD — <b>per-tenant</b> (standart IMultiTenant; her tenant yalnız kendi şirketlerini
/// yönetir). BaseCurrencyUnitId görünür bir <see cref="CurrencyUnit"/> olmalı (global + own);
/// birim kodu okuma anında join'lenir (filter-disable). Şirket = OrgScope üstü + değerleme base'i.
/// </summary>
[Authorize(TradeXpressPermissions.Companies.Default)]
public class CompanyAppService : TradeXpressAppService, ICompanyAppService
{
    private readonly IRepository<Company, Guid> _repository;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Vault, Guid> _vaultRepository;
    private readonly IDataFilter _dataFilter;
    private readonly OrgTreeManager _orgTree;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Name", "CountryCode", "IsActive", "DisplayOrder", "Id" };

    public CompanyAppService(
        IRepository<Company, Guid> repository,
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Vault, Guid> vaultRepository,
        IDataFilter dataFilter,
        OrgTreeManager orgTree)
    {
        _repository = repository;
        _unitRepository = unitRepository;
        _branchRepository = branchRepository;
        _vaultRepository = vaultRepository;
        _dataFilter = dataFilter;
        _orgTree = orgTree;
    }

    public virtual async Task<PagedResultDto<CompanyListDto>> GetListAsync(CompanyListRequestDto input)
    {
        var query = (await _repository.GetQueryableAsync()).ApplyListRequest(input, AllowedListFields);
        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var codes = await LoadCurrencyCodesAsync(items.Select(c => c.BaseCurrencyUnitId));
        return new PagedResultDto<CompanyListDto>(
            totalCount,
            items.Select(c => new CompanyListDto
            {
                Id = c.Id,
                Name = c.Name,
                CountryCode = c.CountryCode,
                BaseCurrencyUnitId = c.BaseCurrencyUnitId,
                BaseCurrencyCode = codes.GetValueOrDefault(c.BaseCurrencyUnitId, string.Empty),
                IsActive = c.IsActive,
                IsHeadquarters = c.IsHeadquarters,
                DisplayOrder = c.DisplayOrder,
            }).ToList());
    }

    public virtual async Task<CompanyGetDto> GetAsync(Guid id)
    {
        var c = await _repository.GetAsync(id);
        var codes = await LoadCurrencyCodesAsync(new[] { c.BaseCurrencyUnitId });
        return ToGetDto(c, codes);
    }

    [Authorize(TradeXpressPermissions.Companies.Create)]
    public virtual async Task<CompanyGetDto> CreateAsync(CompanyCreateDto input)
    {
        // Şirket/şube TENANT'a aittir — host (merkezi operasyon) şirket tanımlayamaz.
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        await EnsureCurrencyVisibleAsync(input.BaseCurrencyUnitId);

        var c = new Company(
            GuidGenerator.Create(),
            input.Name,
            input.CountryCode,
            input.BaseCurrencyUnitId,
            isHeadquarters: input.IsHeadquarters,
            displayOrder: input.DisplayOrder,
            tenantId: CurrentTenant.Id);
        c.SetDescription(input.Description);

        await _repository.InsertAsync(c, autoSave: true);

        // Tek-HQ değişmezi: bu şirket HQ ise tenant'ın önceki HQ'sunu düşür.
        if (c.IsHeadquarters)
            await UnsetOtherHeadquartersAsync(c.Id);

        // En az 1 child: her şirket otomatik bir merkez (HQ) şubeyle (ve onun varsayılan kasasıyla) doğar.
        await _orgTree.EnsureHeadquartersBranchAsync(c);

        var codes = await LoadCurrencyCodesAsync(new[] { c.BaseCurrencyUnitId });
        return ToGetDto(c, codes);
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

        c.SetName(input.Name);
        c.SetCountryCode(input.CountryCode);
        c.SetBaseCurrency(input.BaseCurrencyUnitId);
        c.SetDescription(input.Description);
        c.SetDisplayOrder(input.DisplayOrder);
        if (input.IsActive) c.Activate(); else c.Deactivate();

        await _repository.UpdateAsync(c, autoSave: true);
        var codes = await LoadCurrencyCodesAsync(new[] { c.BaseCurrencyUnitId });
        return ToGetDto(c, codes);
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

    // ── tree (in-memory commit) ─────────────────────────────────────────────────

    public virtual async Task<CompanyTreeDto> GetTreeAsync(Guid id)
    {
        var c = await _repository.GetAsync(id);
        var codes = await LoadCurrencyCodesAsync(new[] { c.BaseCurrencyUnitId });

        var branches = await AsyncExecuter.ToListAsync((await _branchRepository.GetQueryableAsync())
            .Where(b => b.CompanyId == id).OrderBy(b => b.DisplayOrder));
        var branchIds = branches.Select(b => b.Id).ToList();
        var vaults = await AsyncExecuter.ToListAsync((await _vaultRepository.GetQueryableAsync())
            .Where(v => branchIds.Contains(v.BranchId)).OrderBy(v => v.DisplayOrder));

        return new CompanyTreeDto
        {
            Id = c.Id,
            Name = c.Name,
            CountryCode = c.CountryCode,
            BaseCurrencyUnitId = c.BaseCurrencyUnitId,
            BaseCurrencyCode = codes.GetValueOrDefault(c.BaseCurrencyUnitId, string.Empty),
            IsActive = c.IsActive,
            IsHeadquarters = c.IsHeadquarters,
            DisplayOrder = c.DisplayOrder,
            Description = c.Description,
            ConcurrencyStamp = c.ConcurrencyStamp,
            Branches = branches.Select(b => new BranchTreeDto
            {
                Id = b.Id,
                Name = b.Name,
                IsHeadquarters = b.IsHeadquarters,
                IsActive = b.IsActive,
                DisplayOrder = b.DisplayOrder,
                Description = b.Description,
                ConcurrencyStamp = b.ConcurrencyStamp,
                Vaults = vaults.Where(v => v.BranchId == b.Id).Select(v => new VaultTreeDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    IsDefault = v.IsDefault,
                    IsActive = v.IsActive,
                    DisplayOrder = v.DisplayOrder,
                    Description = v.Description,
                    ConcurrencyStamp = v.ConcurrencyStamp,
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary>
    /// Tüm ağacı tek transaction'da kaydeder. Güvenlik/bütünlük için: (a) diff'e göre granüler izin
    /// kontrolü (Branches/Vaults Create/Update/Delete), (b) per-entity ConcurrencyStamp ile optimistic
    /// concurrency (eşzamanlı düzenleme AbpDbConcurrencyException atar), (c) KÖR omission-delete YOK —
    /// yalnız kullanıcının açıkça kaldırdığı (DeletedBranchIds/DeletedVaultIds) öğeler silinir, böylece
    /// eşzamanlı eklenen kardeşler korunur, (d) HQ şube ancak başka bir kalan şubeye devredildiyse
    /// silinebilir, (e) tüm yazmalar tek ambient UoW içinde atomiktir (hata → tüm ağaç geri alınır).
    /// <para>
    /// MİMARİ NOT: Bu snapshot + açık DeletedXIds modeli BİLİNÇLİ tercihtir; in-memory delta tracker
    /// (UiChangeTracker) + server-side sayfalı vitrin + optimistic merge ERTELENDİ — bugün DrillList'in
    /// tek tüketicisi küçük & bounded Company→Şube→Kasa ağacı. Yeniden değerlendirme tetikleyicileri:
    /// (i) ~500+ child'lı doğrulanmış yeni entity, (ii) DrillList 1000 guardrail'i production'da fiilen
    /// tetiklenirse, (iii) SaveTree payload/latency ölçülebilir bozulursa, (iv) DrillList ikinci
    /// bounded-olmayan tüketici kazanırsa. Stamp UPDATE dallarında fail-CLOSED (boş stamp → TreeChanged).
    /// </para>
    /// </summary>
    public virtual async Task<CompanyTreeDto> SaveTreeAsync(CompanyTreeSaveDto input)
    {
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");
        await EnsureCurrencyVisibleAsync(input.BaseCurrencyUnitId);

        var isNew = input.Id is null || input.Id == Guid.Empty;

        // (a) Diff'e göre granüler izin kontrolü — class-level Companies.Default'a güvenme.
        await AuthorizeTreeAsync(input, isNew);

        // 1) Şirket upsert + tek-HQ değişmezi.
        Company company;
        if (isNew)
        {
            company = new Company(GuidGenerator.Create(), input.Name, input.CountryCode, input.BaseCurrencyUnitId,
                isHeadquarters: input.IsHeadquarters, displayOrder: input.DisplayOrder, tenantId: CurrentTenant.Id);
            company.SetDescription(input.Description);
            await _repository.InsertAsync(company, autoSave: true);
        }
        else
        {
            company = await _repository.GetAsync(input.Id!.Value);
            // Fail-CLOSED optimistik kilit: mevcut kayıt güncellenirken stamp ZORUNLU. Boşsa client bayat/
            // forge edilmiş bir payload gönderiyordur → sessizce ezme, reddet. (Yeni kayıtta stamp beklenmez.)
            if (string.IsNullOrEmpty(input.ConcurrencyStamp))
                throw new BusinessException("TradeXpress:Company:TreeChanged");
            company.ConcurrencyStamp = input.ConcurrencyStamp;
            if (input.IsHeadquarters && !company.IsHeadquarters)
                company.SetAsHeadquarters(true);
            else if (!input.IsHeadquarters && company.IsHeadquarters)
                throw new BusinessException("TradeXpress:Company:CannotUnsetHeadquarters");
            company.SetName(input.Name);
            company.SetCountryCode(input.CountryCode);
            company.SetBaseCurrency(input.BaseCurrencyUnitId);
            company.SetDescription(input.Description);
            company.SetDisplayOrder(input.DisplayOrder);
            if (input.IsActive) company.Activate(); else company.Deactivate();
            await _repository.UpdateAsync(company, autoSave: true);
        }
        if (company.IsHeadquarters)
            await UnsetOtherHeadquartersAsync(company.Id, autoSave: true);

        var existingBranches = isNew
            ? new List<Branch>()
            : await AsyncExecuter.ToListAsync((await _branchRepository.GetQueryableAsync()).Where(b => b.CompanyId == company.Id));

        // 2) Şube diff — DisplayOrder'a göre sırala (deterministik HQ seçimi), en az 1 şube + tek HQ.
        var inputBranches = (input.Branches ?? new List<BranchTreeSaveDto>())
            .OrderBy(b => b.DisplayOrder).ToList();
        if (inputBranches.Count == 0)
            inputBranches.Add(new BranchTreeSaveDto { Name = BranchConsts.DefaultHeadquartersName, IsHeadquarters = true, DisplayOrder = 1 });
        // Kullanıcının AÇIKÇA bir HQ işaretleyip işaretlemediğini normalize'den ÖNCE yakala
        // (HQ devri kontrolü için: HQ silinirken kalanlardan biri açıkça HQ olmalı, otomatik terfi sayılmaz).
        var explicitSurvivingHq = inputBranches.Any(b => b.IsHeadquarters);
        NormalizeSingleFlag(inputBranches, b => b.IsHeadquarters, (b, v) => b.IsHeadquarters = v, forceOne: true);

        var keptBranchIds = new HashSet<Guid>();
        foreach (var bi in inputBranches)
        {
            Branch branch;
            var bNew = isNew || bi.Id is null || bi.Id == Guid.Empty;  // yeni şirkette tüm çocuklar yeni
            if (bNew)
            {
                branch = new Branch(GuidGenerator.Create(), company.Id, bi.Name,
                    isHeadquarters: bi.IsHeadquarters, displayOrder: bi.DisplayOrder, tenantId: CurrentTenant.Id);
                branch.SetDescription(bi.Description);
                await _branchRepository.InsertAsync(branch, autoSave: true);
            }
            else
            {
                branch = existingBranches.FirstOrDefault(x => x.Id == bi.Id!.Value)
                    ?? throw new BusinessException("TradeXpress:Company:TreeChanged");  // stale/forged child Id
                if (string.IsNullOrEmpty(bi.ConcurrencyStamp))  // fail-closed: mevcut şube stamp'siz güncellenemez
                    throw new BusinessException("TradeXpress:Company:TreeChanged");
                branch.ConcurrencyStamp = bi.ConcurrencyStamp;
                branch.SetName(bi.Name);
                branch.SetAsHeadquarters(bi.IsHeadquarters);
                branch.SetDescription(bi.Description);
                branch.SetDisplayOrder(bi.DisplayOrder);
                if (bi.IsActive) branch.Activate(); else branch.Deactivate();
                await _branchRepository.UpdateAsync(branch, autoSave: true);
            }
            keptBranchIds.Add(branch.Id);

            // 3) Kasa diff — en az 1 kasa + TEK varsayılan (forceOne:true, simetrik invariant).
            var existingVaults = bNew
                ? new List<Vault>()
                : await AsyncExecuter.ToListAsync((await _vaultRepository.GetQueryableAsync()).Where(v => v.BranchId == branch.Id));

            var inputVaults = (bi.Vaults ?? new List<VaultTreeSaveDto>())
                .OrderBy(v => v.DisplayOrder).ToList();
            if (inputVaults.Count == 0)
                inputVaults.Add(new VaultTreeSaveDto { Name = VaultConsts.DefaultName, IsDefault = true, DisplayOrder = 1 });
            NormalizeSingleFlag(inputVaults, v => v.IsDefault, (v, val) => v.IsDefault = val, forceOne: true);

            var keptVaultIds = new HashSet<Guid>();
            foreach (var vi in inputVaults)
            {
                Vault vault;
                if (bNew || vi.Id is null || vi.Id == Guid.Empty)
                {
                    vault = new Vault(GuidGenerator.Create(), branch.Id, vi.Name,
                        isDefault: vi.IsDefault, displayOrder: vi.DisplayOrder, tenantId: CurrentTenant.Id);
                    vault.SetDescription(vi.Description);
                    await _vaultRepository.InsertAsync(vault, autoSave: true);
                }
                else
                {
                    vault = existingVaults.FirstOrDefault(x => x.Id == vi.Id!.Value)
                        ?? throw new BusinessException("TradeXpress:Company:TreeChanged");
                    if (string.IsNullOrEmpty(vi.ConcurrencyStamp))  // fail-closed: mevcut kasa stamp'siz güncellenemez
                        throw new BusinessException("TradeXpress:Company:TreeChanged");
                    vault.ConcurrencyStamp = vi.ConcurrencyStamp;
                    vault.SetName(vi.Name);
                    vault.SetAsDefault(vi.IsDefault);
                    vault.SetDescription(vi.Description);
                    vault.SetDisplayOrder(vi.DisplayOrder);
                    if (vi.IsActive) vault.Activate(); else vault.Deactivate();
                    await _vaultRepository.UpdateAsync(vault, autoSave: true);
                }
                keptVaultIds.Add(vault.Id);
            }

            // (c) Yalnız açıkça kaldırılan kasaları sil (kör omission değil).
            if (!bNew && bi.DeletedVaultIds is { Count: > 0 })
            {
                foreach (var ev in existingVaults.Where(v => bi.DeletedVaultIds.Contains(v.Id) && !keptVaultIds.Contains(v.Id)))
                    await _vaultRepository.DeleteAsync(ev, autoSave: true);
            }
        }

        // (c)+(d) Yalnız açıkça kaldırılan şubeleri sil; HQ şube ancak devredildiyse silinebilir.
        if (!isNew && input.DeletedBranchIds is { Count: > 0 })
        {
            foreach (var eb in existingBranches.Where(b => input.DeletedBranchIds.Contains(b.Id) && !keptBranchIds.Contains(b.Id)))
            {
                if (eb.IsHeadquarters && !explicitSurvivingHq)
                    throw new BusinessException("TradeXpress:Branch:CannotDeleteHeadquarters");
                await _orgTree.DeleteVaultsOfBranchAsync(eb.Id, autoSave: true);
                await _branchRepository.DeleteAsync(eb, autoSave: true);
            }
        }

        // Tüm yazmalar ambient UoW içinde (atomik): bir hata olursa tüm ağaç geri alınır.
        return await GetTreeAsync(company.Id);
    }

    /// <summary>Tree-save'in yapacağı işlemlere göre gereken granüler izinleri kontrol eder.</summary>
    private async Task AuthorizeTreeAsync(CompanyTreeSaveDto input, bool isNew)
    {
        await AuthorizationService.CheckAsync(isNew
            ? TradeXpressPermissions.Companies.Create
            : TradeXpressPermissions.Companies.Update);

        var branches = input.Branches ?? new List<BranchTreeSaveDto>();
        if (isNew || branches.Any(b => b.Id is null || b.Id == Guid.Empty))
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Branches.Create);
        if (!isNew && branches.Any(b => b.Id is { } id && id != Guid.Empty))
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Branches.Update);
        if (input.DeletedBranchIds is { Count: > 0 })
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Branches.Delete);

        var vaults = branches.SelectMany(b => b.Vaults ?? new List<VaultTreeSaveDto>()).ToList();
        if (isNew || vaults.Any(v => v.Id is null || v.Id == Guid.Empty))
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Vaults.Create);
        if (!isNew && vaults.Any(v => v.Id is { } id && id != Guid.Empty))
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Vaults.Update);
        if (branches.Any(b => b.DeletedVaultIds is { Count: > 0 }))
            await AuthorizationService.CheckAsync(TradeXpressPermissions.Vaults.Delete);
    }

    /// <summary>Listede flag'i (HQ/varsayılan) tekilleştirir. Liste DisplayOrder'a göre sıralı
    /// gelmeli (deterministik seçim). forceOne=true ise hiç yoksa ilkini işaretler.</summary>
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

    private static CompanyGetDto ToGetDto(Company c, Dictionary<Guid, string> codes) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CountryCode = c.CountryCode,
        BaseCurrencyUnitId = c.BaseCurrencyUnitId,
        BaseCurrencyCode = codes.GetValueOrDefault(c.BaseCurrencyUnitId, string.Empty),
        IsActive = c.IsActive,
        IsHeadquarters = c.IsHeadquarters,
        DisplayOrder = c.DisplayOrder,
        Description = c.Description,
    };
}
