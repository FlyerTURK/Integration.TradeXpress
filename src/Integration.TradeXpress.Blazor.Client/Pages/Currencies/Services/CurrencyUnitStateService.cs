using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Blazor.Client.Pages.Currencies.Models;
using Integration.TradeXpress.Currencies;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Pages.Currencies.Services;

/// <summary>CurrencyUnit CRUD ekranı için UI State (Durum) yöneticisi.</summary>
[ExposeServices(
    typeof(ICrudStateService<CurrencyUnitGetDto, CurrencyUnitListDto, Guid, CurrencyUnitViewModel>),
    typeof(CurrencyUnitStateService))]
public class CurrencyUnitStateService : CrudStateServiceBase<CurrencyUnitGetDto, CurrencyUnitListDto, Guid, CurrencyUnitViewModel>
{
}
