using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Pages.Admin.Models;
using Volo.Abp.Application.Services;
using Integration.Framework.Base.Querying;

namespace Integration.TradeXpress.Blazor.Client.Services.Identity;

// ABP Identity AppService'lerini, framework'ün CrudPageBase/CrudLayout'unun beklediği
// ICrudAppService<...> sözleşmesine uyarlar. Veri kümesi küçük (admin kullanıcı/rol listesi)
// olduğundan filtre/sıralama/sayfalama bellek içinde uygulanır.

public class UserCrudAdapter
    : ICrudAppService<UserGetDto, UserListDto, Guid, UserListRequestDto, CreateIdentityUserInput, UpdateIdentityUserInput>
{
    private readonly IIdentityUserService _svc;

    public UserCrudAdapter(IIdentityUserService svc) => _svc = svc;

    public async Task<Volo.Abp.Application.Dtos.PagedResultDto<UserListDto>> GetListAsync(UserListRequestDto input)
    {
        var all = await _svc.GetListAsync(new GetIdentityUsersInput
        {
            MaxResultCount = 1000,
            Filter = input.Filter ?? string.Empty
        });

        IEnumerable<IdentityUserDto> q = all.Items;

        if (input.IsActive.HasValue)
            q = q.Where(u => u.IsActive == input.IsActive.Value);

        q = ApplySort(q, input);

        var list = q.Select(MapList).ToList();
        var total = list.Count;
        var page = list.ApplyPaging(input).ToList();
        return new Volo.Abp.Application.Dtos.PagedResultDto<UserListDto>(total, page);
    }

    private static IEnumerable<IdentityUserDto> ApplySort(IEnumerable<IdentityUserDto> q, UserListRequestDto input)
    {
        var sort = input.Sorts?.FirstOrDefault();
        if (sort == null) return q.OrderBy(u => u.UserName);
        Func<IdentityUserDto, object?> key = sort.Field?.ToLowerInvariant() switch
        {
            "name" => u => u.Name,
            "surname" => u => u.Surname,
            "email" => u => u.Email,
            "phonenumber" => u => u.PhoneNumber,
            "isactive" => u => u.IsActive,
            _ => u => u.UserName
        };
        return sort.Descending ? q.OrderByDescending(key) : q.OrderBy(key);
    }

    public async Task<UserGetDto> GetAsync(Guid id) => MapGet(await _svc.GetAsync(id));

    public async Task<UserGetDto> CreateAsync(CreateIdentityUserInput input) => MapGet(await _svc.CreateAsync(input));

    public async Task<UserGetDto> UpdateAsync(Guid id, UpdateIdentityUserInput input)
    {
        await _svc.UpdateAsync(id, input);
        return await GetAsync(id);
    }

    public Task DeleteAsync(Guid id) => _svc.DeleteAsync(id);

    private static UserListDto MapList(IdentityUserDto u) => new()
    {
        Id = u.Id, UserName = u.UserName, Name = u.Name, Surname = u.Surname,
        Email = u.Email, PhoneNumber = u.PhoneNumber, IsActive = u.IsActive
    };

    private static UserGetDto MapGet(IdentityUserDto u) => new()
    {
        Id = u.Id, UserName = u.UserName, Name = u.Name, Surname = u.Surname,
        Email = u.Email, PhoneNumber = u.PhoneNumber, IsActive = u.IsActive,
        ConcurrencyStamp = u.ConcurrencyStamp
    };
}

public class RoleCrudAdapter
    : ICrudAppService<RoleGetDto, RoleListDto, Guid, RoleListRequestDto, CreateIdentityRoleInput, UpdateIdentityRoleInput>
{
    private readonly IIdentityRoleService _svc;

    public RoleCrudAdapter(IIdentityRoleService svc) => _svc = svc;

    public async Task<Volo.Abp.Application.Dtos.PagedResultDto<RoleListDto>> GetListAsync(RoleListRequestDto input)
    {
        var all = await _svc.GetListAsync(new GetIdentityRolesInput
        {
            MaxResultCount = 1000,
            Filter = input.Filter ?? string.Empty
        });

        IEnumerable<IdentityRoleDto> q = all.Items;

        var sort = input.Sorts?.FirstOrDefault();
        if (sort == null)
            q = q.OrderBy(r => r.Name);
        else
        {
            Func<IdentityRoleDto, object?> key = sort.Field?.ToLowerInvariant() switch
            {
                "isdefault" => r => r.IsDefault,
                "ispublic" => r => r.IsPublic,
                "isstatic" => r => r.IsStatic,
                _ => r => r.Name
            };
            q = sort.Descending ? q.OrderByDescending(key) : q.OrderBy(key);
        }

        var list = q.Select(MapList).ToList();
        var total = list.Count;
        var page = list.ApplyPaging(input).ToList();
        return new Volo.Abp.Application.Dtos.PagedResultDto<RoleListDto>(total, page);
    }

    public async Task<RoleGetDto> GetAsync(Guid id) => MapGet(await _svc.GetAsync(id));

    public async Task<RoleGetDto> CreateAsync(CreateIdentityRoleInput input) => MapGet(await _svc.CreateAsync(input));

    public async Task<RoleGetDto> UpdateAsync(Guid id, UpdateIdentityRoleInput input)
    {
        await _svc.UpdateAsync(id, input);
        return await GetAsync(id);
    }

    public Task DeleteAsync(Guid id) => _svc.DeleteAsync(id);

    private static RoleListDto MapList(IdentityRoleDto r) => new()
    {
        Id = r.Id, Name = r.Name, IsDefault = r.IsDefault, IsPublic = r.IsPublic, IsStatic = r.IsStatic
    };

    private static RoleGetDto MapGet(IdentityRoleDto r) => new()
    {
        Id = r.Id, Name = r.Name, IsDefault = r.IsDefault, IsPublic = r.IsPublic, IsStatic = r.IsStatic,
        ConcurrencyStamp = r.ConcurrencyStamp
    };
}
