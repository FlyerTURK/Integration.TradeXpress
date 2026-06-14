using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Countries.Models;
using Integration.TradeXpress.Countries;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.Countries.Services;

/// <summary>Country CRUD ekranı için UI State yöneticisi.</summary>
[ExposeServices(
    typeof(ICrudStateService<CountryGetDto, CountryListDto, Guid, CountryViewModel>),
    typeof(CountryStateService))]
public class CountryStateService : CrudStateServiceBase<CountryGetDto, CountryListDto, Guid, CountryViewModel>
{
}
