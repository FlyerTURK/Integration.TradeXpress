using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Financials.ExchangeRates;

public interface IExchangeRateAppService : IApplicationService
{
    Task<List<LiveRateDto>> GetLiveRatesAsync();
}
