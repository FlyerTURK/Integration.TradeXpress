using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Branches.Models;
using Integration.TradeXpress.Branches;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.Branches.Services;

/// <summary>Branch CRUD ekranı için UI State yöneticisi.</summary>
[ExposeServices(
    typeof(ICrudStateService<BranchGetDto, BranchListDto, Guid, BranchViewModel>),
    typeof(BranchStateService))]
public class BranchStateService : CrudStateServiceBase<BranchGetDto, BranchListDto, Guid, BranchViewModel>
{
}
