using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Authorization;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Vaults;

/// <summary>
/// Vault (kasa) CRUD — <b>per-tenant</b>. Parent şube aynı tenant'ta görünür olmalı; şube adı okuma
/// anında join'lenir. Değişmezler: şube başına en çok bir varsayılan kasa (set → diğerini düşür);
/// şubenin son kasası silinemez (en az 1 child kuralı).
/// </summary>
[Authorize(TradeXpressPermissions.Vaults.Default)]
public class VaultAppService : TradeXpressAppService, IVaultAppService
{
    private readonly IRepository<Vault, Guid> _repository;
    private readonly IRepository<Branch, Guid> _branchRepository;
    private readonly IRepository<Company, Guid> _companyRepository;
    private readonly ICurrentCompany _currentCompany;
    private readonly IScopedGrantResolver _scopedGrantResolver;   // working-context kasa daraltması (yalnız OKUMA)
    private readonly IDataFilter _dataFilter;

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "BranchCode", "IsDefault", "IsActive", "DisplayOrder", "BranchId", "Id" };

    public VaultAppService(
        IRepository<Vault, Guid> repository,
        IRepository<Branch, Guid> branchRepository,
        IRepository<Company, Guid> companyRepository,
        ICurrentCompany currentCompany,
        IScopedGrantResolver scopedGrantResolver,
        IDataFilter dataFilter)
    {
        _scopedGrantResolver = scopedGrantResolver;
        _dataFilter = dataFilter;
        _repository = repository;
        _branchRepository = branchRepository;
        _companyRepository = companyRepository;
        _currentCompany = currentCompany;
    }

    // NOT (2026-07-15 ürün kararı): burada GetCurrentAccountAsync vardı — seçilen kasayı SAHTE bir cariye
    // (vault-cari) çözüyordu. Emekli edildi: kasa artık fişte DOĞRUDAN karşı taraftır (AccountType=Vault;
    // Şube→AccountId/AccountCode, Kasa→SubAccountId/SubAccountCode) → cari üretilmez, cari listesi kirlenmez.

    /// <summary>Sızıntı önleme: CompanyId DAİMA working-context'ten zorlanır (client'a güvenilmez).</summary>
    private Guid EnsureCurrentCompanyId()
    {
        if (_currentCompany.Id is not { } companyId)
        {
            throw new BusinessException("TradeXpress:Vault:CompanyContextRequired");
        }

        return companyId;
    }

    public virtual async Task<PagedResultDto<VaultListDto>> GetListAsync(VaultListRequestDto input)
    {
        var branches = await _branchRepository.GetQueryableAsync();
        var query = await _repository.GetQueryableAsync();
        if (input.BranchId.HasValue)
            query = query.Where(v => v.BranchId == input.BranchId.Value);

        // BranchCode enrichment'tı → join ile GERÇEK kolon yap: kod ile sort/filter/arama server-side çalışsın.
        var rows = query
            .Join(branches, v => v.BranchId, b => b.Id, (v, b) => new VaultListRow
            {
                Id = v.Id,
                BranchId = v.BranchId,
                BranchCode = b.Code,
                Code = v.Code,
                Name = v.Name,
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
            })
            .ApplyListRequest(input, AllowedListFields);

        var totalCount = await AsyncExecuter.CountAsync(rows);
        var items = await AsyncExecuter.ToListAsync(rows.ApplyPaging(input));

        return new PagedResultDto<VaultListDto>(
            totalCount,
            items.Select(r => new VaultListDto
            {
                Id = r.Id,
                BranchId = r.BranchId,
                BranchCode = r.BranchCode,
                Code = r.Code,
                Name = r.Name,
                IsDefault = r.IsDefault,
                IsActive = r.IsActive,
                DisplayOrder = r.DisplayOrder,
            }).ToList());
    }

    /// <summary>
    /// Kullanıcının ÇALIŞABİLDİĞİ kasalar — <c>BranchAppService.GetMyBranchesAsync</c>'in kasa AYNASI:
    /// server-side kapsam (scope) daraltması, her satır <see cref="ScopedAccessSet.CanAccessVault"/> ile elenir
    /// (en-spesifik-kazanır). Kendi çalışma kapsamının okunması → yalnız kimliklendirilmiş kullanıcı yeter.
    ///
    /// <para><b>ŞİRKET FİLTRESİ GEVŞETMESİ (yalnız BU OKUMADA — onaylı):</b> <see cref="Vault"/>
    /// <c>ICompanyOwned</c>'dır (global company query-filter'a girer) ama <c>Branch</c> değildir. Filtre açık
    /// kalsaydı kullanıcı yalnız AKTİF şirketinin kasalarını görür, seçiciden başka şirkete GEÇEMEZ, yani
    /// kendini aktif şirkete KİLİTLERDİ (şirket seçimi de bu combo'dan yapılır — tavuk-yumurta). Gevşetme
    /// güvenlik açmaz: satırlar yine kapsam-grant'i ile elenir; tenant filtresi AÇIK kalır; bu bir salt-OKUMA
    /// yoludur (yazma yolu yok). Yetkiyi filtre değil grant belirler.</para>
    /// </summary>
    [Authorize]
    public virtual async Task<List<MyVaultDto>> GetMyVaultsAsync(Guid? branchId = null)
    {
        var access = await _scopedGrantResolver.ResolveAsync(CurrentUser.GetId());

        List<MyVaultRow> rows;
        using (_dataFilter.Disable<ICompanyScoped>())   // yalnız company filtresi (tenant filtresi AÇIK kalır)
        {
            var vaults = await _repository.GetQueryableAsync();
            if (branchId.HasValue)
                vaults = vaults.Where(v => v.BranchId == branchId.Value);

            var branches = await _branchRepository.GetQueryableAsync();
            var companies = await _companyRepository.GetQueryableAsync();

            var query = vaults
                .Join(branches, v => v.BranchId, b => b.Id, (v, b) => new { v, b })
                .Join(companies, x => x.b.CompanyId, c => c.Id, (x, c) => new MyVaultRow
                {
                    Id = x.v.Id,
                    CompanyId = c.Id,
                    CompanyCode = c.Code,
                    CompanyName = c.Name,
                    BranchId = x.b.Id,
                    BranchCode = x.b.Code,
                    BranchName = x.b.Name,
                    Code = x.v.Code,
                    Name = x.v.Name,
                    IsDefault = x.v.IsDefault,
                    IsActive = x.v.IsActive,
                    DisplayOrder = x.v.DisplayOrder,
                });

            rows = await AsyncExecuter.ToListAsync(query);
        }

        return rows
            .Where(r => r.IsActive && access.CanAccessVault(r.CompanyId, r.BranchId, r.Id))
            .OrderBy(r => r.CompanyCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.BranchCode, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(r => r.IsDefault)
            .ThenBy(r => r.DisplayOrder)
            .ThenBy(r => r.Code, StringComparer.OrdinalIgnoreCase)
            .Select(r => new MyVaultDto
            {
                Id = r.Id,
                CompanyId = r.CompanyId,
                CompanyCode = r.CompanyCode,
                CompanyName = r.CompanyName,
                BranchId = r.BranchId,
                BranchCode = r.BranchCode,
                BranchName = r.BranchName,
                Code = r.Code,
                Name = r.Name,
                IsDefault = r.IsDefault,
                DisplayOrder = r.DisplayOrder,
            })
            .ToList();
    }

    public virtual async Task<VaultGetDto> GetAsync(Guid id)
    {
        var v = await _repository.GetAsync(id);
        var names = await LoadBranchCodesAsync(new[] { v.BranchId });
        return ToGetDto(v, names);
    }

    [Authorize(TradeXpressPermissions.Vaults.Create)]
    public virtual async Task<VaultGetDto> CreateAsync(VaultCreateDto input)
    {
        if (CurrentTenant.Id == null)
            throw new BusinessException("TradeXpress:Company:HostHasNoCompanies");

        // Güvenlik sınırı: CompanyId client'tan DEĞİL, görünür parent şubeden DENORMALİZE edilir
        // (Branch tenant-scoped görünür → yabancı şubeden türetme sızmaz).
        var branch = await EnsureBranchVisibleAsync(input.BranchId);

        // Benzersizlik ÖN-kontrolü (şube scope): aynı şubede aynı kodlu kasa → dostane hata (Update'le simetrik).
        var normalizedCode = StringFieldGuard.NormalizeCode(
            input.Code, nameof(Vault.Code), EntityFieldConsts.CodeMinLength, VaultConsts.CodeMaxLength);
        await EnsureCodeUniqueAsync(branch.Id, normalizedCode, Guid.Empty);

        var v = new Vault(
            branch.CompanyId,
            branch.Id,
            input.Code,
            input.Name,
            isDefault: input.IsDefault,
            displayOrder: input.DisplayOrder);
        v.SetDescription(input.Description);

        await _repository.InsertAsync(v, autoSave: true);

        if (v.IsDefault)
            await UnsetOtherDefaultsAsync(v.BranchId, v.Id);

        var names = await LoadBranchCodesAsync(new[] { v.BranchId });
        return ToGetDto(v, names);
    }

    [Authorize(TradeXpressPermissions.Vaults.Update)]
    public virtual async Task<VaultGetDto> UpdateAsync(Guid id, VaultUpdateDto input)
    {
        var v = await _repository.GetAsync(id);

        await ApplyCodeChangeAsync(v, input.Code);
        v.SetName(input.Name);
        v.SetDescription(input.Description);
        v.SetDisplayOrder(input.DisplayOrder);
        v.SetAsDefault(input.IsDefault);
        v.SetActive(input.IsActive);

        await _repository.UpdateAsync(v, autoSave: true);

        if (v.IsDefault)
            await UnsetOtherDefaultsAsync(v.BranchId, v.Id);

        var names = await LoadBranchCodesAsync(new[] { v.BranchId });
        return ToGetDto(v, names);
    }

    [Authorize(TradeXpressPermissions.Vaults.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var v = await _repository.GetAsync(id);

        var siblingCount = await AsyncExecuter.CountAsync(
            (await _repository.GetQueryableAsync()).Where(x => x.BranchId == v.BranchId));
        if (siblingCount <= 1)
            throw new BusinessException("TradeXpress:Vault:BranchMustHaveVault");

        await _repository.DeleteAsync(v, autoSave: true);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>Kod değişikliği (kod düzenlenebilir ürün kuralı): normalize → değiştiyse AYNI ŞUBE altında
    /// benzersizliği doğrula (kendisi hariç; dostane hata) → uygula.</summary>
    private async Task ApplyCodeChangeAsync(Vault v, string rawCode)
    {
        var normalizedCode = StringFieldGuard.NormalizeCode(
            rawCode, nameof(v.Code), EntityFieldConsts.CodeMinLength, VaultConsts.CodeMaxLength);
        if (string.Equals(normalizedCode, v.Code, StringComparison.Ordinal))
        {
            return; // değişmedi
        }

        await EnsureCodeUniqueAsync(v.BranchId, normalizedCode, v.Id);
        v.SetCode(normalizedCode);
    }

    /// <summary>Aynı ŞUBE altında Code benzersizliği. Create'te <paramref name="excludeId"/>=Guid.Empty,
    /// Update'te v.Id. Dostane BusinessException — ham DB unique çakışmasını önler.</summary>
    private async Task EnsureCodeUniqueAsync(Guid branchId, string normalizedCode, Guid excludeId)
    {
        var duplicate = await AsyncExecuter.AnyAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.BranchId == branchId && x.Id != excludeId && x.Code == normalizedCode));
        if (duplicate)
        {
            throw new BusinessException("TradeXpress:Vault:CodeAlreadyExists");
        }
    }

    private async Task<Branch> EnsureBranchVisibleAsync(Guid branchId)
    {
        if (await _branchRepository.FindAsync(branchId) is not { } branch)
            throw new EntityNotFoundException(typeof(Branch), branchId);
        return branch;
    }

    private async Task UnsetOtherDefaultsAsync(Guid branchId, Guid exceptVaultId)
    {
        var others = await AsyncExecuter.ToListAsync((await _repository.GetQueryableAsync())
            .Where(x => x.BranchId == branchId && x.IsDefault && x.Id != exceptVaultId));
        foreach (var o in others)
        {
            o.SetAsDefault(false);
            await _repository.UpdateAsync(o, autoSave: true);
        }
    }

    private async Task<Dictionary<Guid, string>> LoadBranchCodesAsync(IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        var q = (await _branchRepository.GetQueryableAsync()).Where(b => list.Contains(b.Id));
        var branches = await AsyncExecuter.ToListAsync(q);
        return branches.ToDictionary(b => b.Id, b => b.Code);
    }

    private static VaultGetDto ToGetDto(Vault v, Dictionary<Guid, string> names) => new()
    {
        Id = v.Id,
        BranchId = v.BranchId,
        BranchCode = names.GetValueOrDefault(v.BranchId, string.Empty),
        Code = v.Code,
        Name = v.Name,
        IsDefault = v.IsDefault,
        IsActive = v.IsActive,
        DisplayOrder = v.DisplayOrder,
        Description = v.Description,
    };

    // Liste projeksiyonu: Vault + join'lenmiş BranchCode (gerçek string kolon → server-side sort/filter/arama).
    private sealed class VaultListRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }

    // Working-context projeksiyonu: Vault + join'lenmiş şube/şirket (combo kolonları tek sorguda).
    private sealed class MyVaultRow
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyCode { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public Guid BranchId { get; set; }
        public string BranchCode { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
    }
}
