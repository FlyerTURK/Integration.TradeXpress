using System;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.Authorization;

/// <summary>Kullanıcının scoped rol/izin ataması (Id'ler taşır; ad/kod UI'da yüklü lookup'lardan çözülür).</summary>
public class UserScopedGrantDto : EntityDto<Guid>
{
    public Guid UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? PermissionName { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }
    public ScopedGrantMode Mode { get; set; }
}

public class UserScopedGrantCreateDto
{
    public Guid UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string? PermissionName { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? VaultId { get; set; }
    public ScopedGrantMode Mode { get; set; } = ScopedGrantMode.Grant;
}
