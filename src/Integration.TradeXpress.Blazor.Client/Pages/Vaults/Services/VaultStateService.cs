using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults.Models;
using Integration.TradeXpress.Vaults;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.Vaults.Services;

/// <summary>Vault CRUD ekranı için UI State yöneticisi.</summary>
[ExposeServices(
    typeof(ICrudStateService<VaultGetDto, VaultListDto, Guid, VaultViewModel>),
    typeof(VaultStateService))]
public class VaultStateService : CrudStateServiceBase<VaultGetDto, VaultListDto, Guid, VaultViewModel>
{
}
