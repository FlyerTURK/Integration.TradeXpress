using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Companies;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies.Services;

/// <summary>Company CRUD ekranı için UI State yöneticisi.</summary>
[ExposeServices(
    typeof(ICrudStateService<CompanyGetDto, CompanyListDto, Guid, CompanyViewModel>),
    typeof(CompanyStateService))]
public class CompanyStateService : CrudStateServiceBase<CompanyGetDto, CompanyListDto, Guid, CompanyViewModel>
{
}
