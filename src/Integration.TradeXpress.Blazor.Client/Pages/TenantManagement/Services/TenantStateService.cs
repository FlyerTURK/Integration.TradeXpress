using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.Models;
using Integration.TradeXpress.Tenants;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.TenantManagement.Services;

/// <summary>
/// Tenant (Kiracı) CRUD operasyonları için UI State (Durum) Yöneticisi.
/// </summary>
[ExposeServices(
    typeof(ICrudStateService<TenantGetDto, TenantListDto, Guid, TenantViewModel>),
    typeof(TenantStateService))]
public class TenantStateService : CrudStateServiceBase<TenantGetDto, TenantListDto, Guid, TenantViewModel>
{
    // Tenant UI'ına özel ekstra state durumları (örneğin admin şifre kontrolü vb.) buraya eklenebilir.
}
