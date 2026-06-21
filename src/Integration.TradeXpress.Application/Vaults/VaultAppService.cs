using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Base.Querying;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

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

    private static readonly HashSet<string> AllowedListFields =
        new(StringComparer.OrdinalIgnoreCase) { "Code", "Name", "IsDefault", "IsActive", "DisplayOrder", "BranchId", "Id" };

    public VaultAppService(
        IRepository<Vault, Guid> repository,
        IRepository<Branch, Guid> branchRepository)
    {
        _repository = repository;
        _branchRepository = branchRepository;
    }

    public virtual async Task<PagedResultDto<VaultListDto>> GetListAsync(VaultListRequestDto input)
    {
        var query = await _repository.GetQueryableAsync();
        if (input.BranchId.HasValue)
            query = query.Where(v => v.BranchId == input.BranchId.Value);
        query = query.ApplyListRequest(input, AllowedListFields);
        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        var names = await LoadBranchCodesAsync(items.Select(v => v.BranchId));
        return new PagedResultDto<VaultListDto>(
            totalCount,
            items.Select(v => new VaultListDto
            {
                Id = v.Id,
                BranchId = v.BranchId,
                BranchCode = names.GetValueOrDefault(v.BranchId, string.Empty),
                Code = v.Code,
                Name = v.Name,
                IsDefault = v.IsDefault,
                IsActive = v.IsActive,
                DisplayOrder = v.DisplayOrder,
            }).ToList());
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

        await EnsureBranchVisibleAsync(input.BranchId);

        var v = new Vault(
            input.BranchId,
            input.Code,
            input.Name,
            isDefault: input.IsDefault,
            displayOrder: input.DisplayOrder,
            tenantId: CurrentTenant.Id);
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

        v.SetCode(input.Code);
        v.SetName(input.Name);
        v.SetDescription(input.Description);
        v.SetDisplayOrder(input.DisplayOrder);
        v.SetAsDefault(input.IsDefault);
        if (input.IsActive) v.Activate(); else v.Deactivate();

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

    private async Task EnsureBranchVisibleAsync(Guid branchId)
    {
        if (await _branchRepository.FindAsync(branchId) == null)
            throw new EntityNotFoundException(typeof(Branch), branchId);
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
}
