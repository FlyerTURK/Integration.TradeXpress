using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Integration.TradeXpress.Blazor.Client.Services;

public interface IIdentityUserService
{
    Task<IdentityUserDto> GetAsync(Guid id);
    Task<PagedResultDto<IdentityUserDto>> GetListAsync(GetIdentityUsersInput input);
    Task<IdentityUserDto> CreateAsync(CreateIdentityUserInput input);
    Task UpdateAsync(Guid id, UpdateIdentityUserInput input);
    Task DeleteAsync(Guid id);
}

public interface IIdentityRoleService
{
    Task<IdentityRoleDto> GetAsync(Guid id);
    Task<PagedResultDto<IdentityRoleDto>> GetListAsync(GetIdentityRolesInput input);
    Task<IdentityRoleDto> CreateAsync(CreateIdentityRoleInput input);
    Task UpdateAsync(Guid id, UpdateIdentityRoleInput input);
    Task DeleteAsync(Guid id);
}

public class IdentityUserService : IIdentityUserService
{
    private readonly Volo.Abp.Identity.IIdentityUserAppService _appService;

    public IdentityUserService(Volo.Abp.Identity.IIdentityUserAppService appService)
    {
        _appService = appService;
    }

    public async Task<IdentityUserDto> GetAsync(Guid id)
    {
        var u = await _appService.GetAsync(id);
        return MapToDto(u);
    }

    public async Task<PagedResultDto<IdentityUserDto>> GetListAsync(GetIdentityUsersInput input)
    {
        var result = await _appService.GetListAsync(new Volo.Abp.Identity.GetIdentityUsersInput
        {
            MaxResultCount = input.MaxResultCount,
            SkipCount = input.SkipCount,
            Filter = input.Filter
        });

        return new PagedResultDto<IdentityUserDto>
        {
            TotalCount = result.TotalCount,
            Items = result.Items.Select(MapToDto).ToList()
        };
    }

    public async Task<IdentityUserDto> CreateAsync(CreateIdentityUserInput input)
    {
        var created = await _appService.CreateAsync(new Volo.Abp.Identity.IdentityUserCreateDto
        {
            UserName = input.UserName,
            Email = input.Email,
            Name = input.Name,
            Surname = input.Surname,
            PhoneNumber = input.PhoneNumber,
            Password = input.Password,
            IsActive = input.IsActive
        });
        return MapToDto(created);
    }

    public async Task UpdateAsync(Guid id, UpdateIdentityUserInput input)
    {
        // UserName is required by ABP's update DTO; carry it from the existing record.
        var existing = await _appService.GetAsync(id);
        await _appService.UpdateAsync(id, new Volo.Abp.Identity.IdentityUserUpdateDto
        {
            UserName = existing.UserName,
            Email = input.Email,
            Name = input.Name,
            Surname = input.Surname,
            PhoneNumber = input.PhoneNumber,
            IsActive = input.IsActive,
            ConcurrencyStamp = input.ConcurrencyStamp
        });
    }

    public Task DeleteAsync(Guid id) => _appService.DeleteAsync(id);

    private static IdentityUserDto MapToDto(Volo.Abp.Identity.IdentityUserDto u) => new()
    {
        Id = u.Id,
        UserName = u.UserName ?? string.Empty,
        Name = u.Name ?? string.Empty,
        Surname = u.Surname ?? string.Empty,
        Email = u.Email ?? string.Empty,
        PhoneNumber = u.PhoneNumber ?? string.Empty,
        IsActive = u.IsActive,
        ConcurrencyStamp = u.ConcurrencyStamp ?? string.Empty
    };
}

public class IdentityRoleService : IIdentityRoleService
{
    private readonly Volo.Abp.Identity.IIdentityRoleAppService _appService;

    public IdentityRoleService(Volo.Abp.Identity.IIdentityRoleAppService appService)
    {
        _appService = appService;
    }

    public async Task<IdentityRoleDto> GetAsync(Guid id)
    {
        var r = await _appService.GetAsync(id);
        return MapToDto(r);
    }

    public async Task<PagedResultDto<IdentityRoleDto>> GetListAsync(GetIdentityRolesInput input)
    {
        var result = await _appService.GetListAsync(new Volo.Abp.Identity.GetIdentityRolesInput
        {
            MaxResultCount = input.MaxResultCount,
            SkipCount = input.SkipCount,
            Filter = input.Filter
        });

        return new PagedResultDto<IdentityRoleDto>
        {
            TotalCount = result.TotalCount,
            Items = result.Items.Select(MapToDto).ToList()
        };
    }

    public async Task<IdentityRoleDto> CreateAsync(CreateIdentityRoleInput input)
    {
        var created = await _appService.CreateAsync(new Volo.Abp.Identity.IdentityRoleCreateDto
        {
            Name = input.Name,
            IsDefault = input.IsDefault,
            IsPublic = input.IsPublic
        });
        return MapToDto(created);
    }

    public async Task UpdateAsync(Guid id, UpdateIdentityRoleInput input)
    {
        await _appService.UpdateAsync(id, new Volo.Abp.Identity.IdentityRoleUpdateDto
        {
            Name = input.Name,
            IsDefault = input.IsDefault,
            IsPublic = input.IsPublic,
            ConcurrencyStamp = input.ConcurrencyStamp
        });
    }

    public Task DeleteAsync(Guid id) => _appService.DeleteAsync(id);

    private static IdentityRoleDto MapToDto(Volo.Abp.Identity.IdentityRoleDto r) => new()
    {
        Id = r.Id,
        Name = r.Name ?? string.Empty,
        IsDefault = r.IsDefault,
        IsPublic = r.IsPublic,
        IsStatic = r.IsStatic,
        ConcurrencyStamp = r.ConcurrencyStamp ?? string.Empty
    };
}

// DTOs
public class IdentityUserDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class IdentityRoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class CreateIdentityUserInput
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class UpdateIdentityUserInput
{
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class CreateIdentityRoleInput
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
}

public class UpdateIdentityRoleInput
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class GetIdentityUsersInput
{
    public int MaxResultCount { get; set; } = 10;
    public int SkipCount { get; set; } = 0;
    public string Filter { get; set; } = string.Empty;
}

public class GetIdentityRolesInput
{
    public int MaxResultCount { get; set; } = 10;
    public int SkipCount { get; set; } = 0;
    public string Filter { get; set; } = string.Empty;
}

public class PagedResultDto<T>
{
    public long TotalCount { get; set; }
    public List<T> Items { get; set; } = new();
}
