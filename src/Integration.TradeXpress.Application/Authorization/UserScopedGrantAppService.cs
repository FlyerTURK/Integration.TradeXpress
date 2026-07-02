using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace Integration.TradeXpress.Authorization;

/// <summary>
/// Kullanıcıya scoped (tenant/company/branch/vault) ROL ya da doğrudan İZİN ataması — Faz 1: CRUD'un
/// depolama/listeleme kısmı. Kullanıcı yetkisini yöneten kişi atar → IdentityPermissions.Users.Update ile gate.
/// Çözümleme + working-context + admin-bypass Faz 2'de.
/// </summary>
[Authorize(IdentityPermissions.Users.Update)]
public class UserScopedGrantAppService : TradeXpressAppService, IUserScopedGrantAppService
{
    private readonly IRepository<UserScopedGrant, Guid> _repository;

    public UserScopedGrantAppService(IRepository<UserScopedGrant, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task<List<UserScopedGrantDto>> GetByUserAsync(Guid userId)
    {
        var query = (await _repository.GetQueryableAsync())
            .Where(g => g.UserId == userId && g.TenantId == CurrentTenant.Id);
        var items = await AsyncExecuter.ToListAsync(query);
        return items.Select(g => ObjectMapper.Map<UserScopedGrant, UserScopedGrantDto>(g)).ToList();
    }

    public virtual async Task<UserScopedGrantDto> CreateAsync(UserScopedGrantCreateDto input)
    {
        // Tutarlılık (RoleId xor PermissionName, scope hiyerarşisi) entity ctor'unda doğrulanır.
        var entity = new UserScopedGrant(
            input.UserId, input.RoleId, input.PermissionName,
            input.CompanyId, input.BranchId, input.VaultId,
            input.Mode);

        await _repository.InsertAsync(entity, autoSave: true);
        return ObjectMapper.Map<UserScopedGrant, UserScopedGrantDto>(entity);
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity.TenantId != CurrentTenant.Id)
            throw new EntityNotFoundException(typeof(UserScopedGrant), id);
        await _repository.DeleteAsync(entity, autoSave: true);
    }
}
