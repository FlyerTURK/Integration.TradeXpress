using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Integration.TradeXpress.Financials.ExchangeRates;

[Authorize]
public class ExchangeRateAppService : TradeXpressAppService, IExchangeRateAppService
{
    private readonly ExchangeRateCacheService _cache;
    private readonly IRepository<CurrencyUnit, Guid> _unitRepo;
    private readonly IRepository<CurrencyUnitMargin, Guid> _marginRepo;

    public ExchangeRateAppService(
        ExchangeRateCacheService cache,
        IRepository<CurrencyUnit, Guid> unitRepo,
        IRepository<CurrencyUnitMargin, Guid> marginRepo)
    {
        _cache      = cache;
        _unitRepo   = unitRepo;
        _marginRepo = marginRepo;
    }

    public async Task<List<LiveRateDto>> GetLiveRatesAsync()
    {
        var quotes = _cache.GetAll();
        if (quotes.Count == 0) return new();

        // code → Id eşlemi
        var units = await _unitRepo.GetListAsync();
        var unitByCode = units.ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);

        // Tenant'ın en güncel marjları (append-only — CreationTime desc, gruplama client-side)
        var allMargins = await _marginRepo.GetListAsync();
        var latestMargin = allMargins
            .GroupBy(m => m.CurrencyUnitId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.CreationTime).First());

        var result = new List<LiveRateDto>(quotes.Count);
        foreach (var (code, quote) in quotes)
        {
            var onBuy  = MarginSetting.Passthrough;
            var onSell = MarginSetting.Passthrough;

            if (unitByCode.TryGetValue(code, out var unit) &&
                latestMargin.TryGetValue(unit.Id, out var margin))
            {
                onBuy  = margin.MarginOnBuy;
                onSell = margin.MarginOnSell;
            }

            var price = CurrencyPriceCalculator.DeriveDirect(quote.Buy, quote.Sell, onBuy, onSell);
            result.Add(new LiveRateDto { Code = code, Buy = price.Buy, Sell = price.Sell });
        }

        return result.OrderBy(r => r.Code).ToList();
    }
}
