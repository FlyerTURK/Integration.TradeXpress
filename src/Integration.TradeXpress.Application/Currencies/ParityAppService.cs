using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Parite panosu motoru. Görünür+aktif pariteleri (null‖own) okur, her çiftin oranını
/// birimlerin <b>efektif fiyatının bid/ask çaprazından</b> (<see cref="CurrencyPriceCalculator.Cross"/>)
/// canlı hesaplar. Parity marjı yok — kademe + gizlilik birim fiyatından otomatik miras.
/// Base/quote'tan en az biri fiyatsızsa (feed gelmemiş) o çift atlanır.
/// </summary>
[Authorize(TradeXpressPermissions.CurrencyUnits.Default)]
public class ParityAppService : TradeXpressAppService, IParityAppService
{
    private readonly IRepository<Parity, Guid> _parityRepository;
    private readonly IEffectivePriceAppService _priceAppService;
    private readonly IDataFilter _dataFilter;

    public ParityAppService(
        IRepository<Parity, Guid> parityRepository,
        IEffectivePriceAppService priceAppService,
        IDataFilter dataFilter)
    {
        _parityRepository = parityRepository;
        _priceAppService = priceAppService;
        _dataFilter = dataFilter;
    }

    public virtual async Task<List<ParityBoardDto>> GetBoardAsync()
    {
        // Birim efektif fiyatları (pivot, viewer kademesi uygulanmış).
        var prices = await _priceAppService.GetCurrentPricesAsync();
        var byUnit = prices.ToDictionary(p => p.Id);

        List<Parity> parities;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var viewer = CurrentTenant.Id;
            parities = await AsyncExecuter.ToListAsync(
                (await _parityRepository.GetQueryableAsync())
                    .Where(p => p.IsActive && (p.TenantId == null || p.TenantId == viewer)));
        }

        var result = new List<ParityBoardDto>();
        foreach (var parity in parities)
        {
            if (!byUnit.TryGetValue(parity.BaseCurrencyUnitId, out var basePx) ||
                !byUnit.TryGetValue(parity.QuoteCurrencyUnitId, out var quotePx))
                continue; // base ya da quote fiyatsız → atla

            var cross = CurrencyPriceCalculator.Cross(
                new CurrencyPrice(basePx.Buy, basePx.Sell, basePx.GuardFired),
                new CurrencyPrice(quotePx.Buy, quotePx.Sell, quotePx.GuardFired));

            result.Add(new ParityBoardDto
            {
                Id = parity.Id,
                Code = basePx.CurrencyUnitCode + quotePx.CurrencyUnitCode,
                BaseCurrencyUnitId = parity.BaseCurrencyUnitId,
                BaseCode = basePx.CurrencyUnitCode,
                QuoteCurrencyUnitId = parity.QuoteCurrencyUnitId,
                QuoteCode = quotePx.CurrencyUnitCode,
                Buy = cross.Buy,
                Sell = cross.Sell,
                GuardFired = cross.GuardFired || basePx.GuardFired || quotePx.GuardFired,
                DisplayOrder = parity.DisplayOrder,
            });
        }

        return result.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Code).ToList();
    }
}
