using System;
using System.ComponentModel.DataAnnotations;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.Framework.Blazor.Client.Components.Crud;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Blazor.Client.Pages.Admin.Models;

// ── Kullanıcı (User) — CrudPageBase/CrudLayout standardı için adapter DTO'ları ──────────
// Gerçek backend ABP'nin IIdentityUserAppService'idir; bu tipler yalnız UI grid/CRUD sözleşmesi
// için framework arayüzlerini (IListDto/IGetDto/IIsActive/IViewModel) uygular.

public class UserListRequestDto : ListRequestDto { }

public class UserListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class UserGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    // Client inline validasyon (yeni EntityEditForm Model'i doğrular) — server otoritesi ABP input'tur.
    [Required]
    [StringLength(256)]
    public string UserName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>Tab modunda edit ayrı sayfada yapıldığından bu VM yalnız generic-kısıt için minimaldir.</summary>
public class UserEditViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }
}

// ── Rol (Role) ──────────────────────────────────────────────────────────────────────────

public class RoleListRequestDto : ListRequestDto { }

public class RoleListDto : EntityDto<Guid>, IListDto<Guid>
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
}

public class RoleGetDto : EntityDto<Guid>, IGetDto<Guid>
{
    // Client inline validasyon (yeni EntityEditForm Model'i doğrular) — server otoritesi ABP input'tur.
    [Required]
    [StringLength(256)]
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class RoleEditViewModel : IViewModel<Guid>
{
    public Guid Id { get; set; }
}
