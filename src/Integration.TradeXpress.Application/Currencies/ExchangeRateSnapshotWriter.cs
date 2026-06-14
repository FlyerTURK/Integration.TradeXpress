using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Currencies;

/// <summary>
/// Bir piyasa snapshot'ını <b>host</b> (TenantId=null) ExchangeRate satırları olarak yazar.
/// Worker'dan ayrı (test edilebilir): köprü olmadan doğrudan kotasyon verilip çağrılabilir.
///
/// <para>Satır HAM piyasa fiyatıdır (AppliedMargin=Passthrough); margin per-tenant okumada uygulanır.
/// İdempotent: aynı (CurrencyUnit, RateDate) penceresi tekrar yazılmaz; fiyat değişmediyse atlanır.
/// Felaket guard'ı ham piyasada uygulanır (ters feed → swap + flag).</para>
/// </summary>
public class ExchangeRateSnapshotWriter : ITransientDependency
{
    private readonly IRepository<CurrencyUnit, Guid> _unitRepository;
    private readonly IRepository<ExchangeRate, Guid> _rateRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IAsyncQueryableExecuter _executer;
    private readonly ICurrentTenant _currentTenant;
    private readonly IOptions<ExchangeRateOptions> _options;
    private readonly ILogger<ExchangeRateSnapshotWriter> _logger;

    public ExchangeRateSnapshotWriter(
        IRepository<CurrencyUnit, Guid> unitRepository,
        IRepository<ExchangeRate, Guid> rateRepository,
        IGuidGenerator guidGenerator,
        IAsyncQueryableExecuter executer,
        ICurrentTenant currentTenant,
        IOptions<ExchangeRateOptions> options,
        ILogger<ExchangeRateSnapshotWriter> logger)
    {
        _unitRepository = unitRepository;
        _rateRepository = rateRepository;
        _guidGenerator = guidGenerator;
        _executer = executer;
        _currentTenant = currentTenant;
        _options = options;
        _logger = logger;
    }

    /// <summary>Verilen kotasyonları host ExchangeRate satırı olarak yazar; yazılan satır sayısını döner.</summary>
    public async Task<int> WriteAsync(
        DateTime rateDate,
        IEnumerable<MarketQuote> quotes,
        CancellationToken cancellationToken = default)
    {
        using (_currentTenant.Change(null)) // host scope
        {
            var unitQuery = await _unitRepository.GetQueryableAsync();
            var rateQuery = await _rateRepository.GetQueryableAsync();
            var persisted = _options.Value.PersistedCodes;
            var written = 0;

            foreach (var quote in quotes)
            {
                if (!persisted.Contains(quote.Code)) continue;
                if (quote.Buy <= 0m || quote.Sell <= 0m) continue;

                var unit = await _executer.FirstOrDefaultAsync(
                    unitQuery.Where(u => u.TenantId == null && u.Code == quote.Code));
                if (unit is null)
                {
                    _logger.LogWarning("Skipping {Code}: no host CurrencyUnit found", quote.Code);
                    continue;
                }

                var slotExists = await _executer.AnyAsync(
                    rateQuery.Where(e => e.CurrencyUnitId == unit.Id && e.RateDate == rateDate));
                if (slotExists) continue;

                var latest = await _executer.FirstOrDefaultAsync(
                    rateQuery.Where(e => e.CurrencyUnitId == unit.Id).OrderByDescending(e => e.RateDate));
                if (latest is not null &&
                    latest.MarketPriceOnBuy == quote.Buy &&
                    latest.MarketPriceOnSell == quote.Sell)
                    continue;

                var guarded = CurrencyPriceCalculator.Guard(quote.Buy, quote.Sell);

                await _rateRepository.InsertAsync(
                    new ExchangeRate(
                        _guidGenerator.Create(),
                        unit.Id,
                        marketPriceOnBuy:  guarded.Buy,
                        marketPriceOnSell: guarded.Sell,
                        appliedMarginOnBuy:  MarginSetting.Passthrough,
                        appliedMarginOnSell: MarginSetting.Passthrough,
                        source:   string.IsNullOrEmpty(quote.Source) ? HaremClient.SourceName : quote.Source,
                        rateDate: rateDate,
                        guardFired: guarded.GuardFired),
                    autoSave: false,
                    cancellationToken);
                written++;
            }

            return written;
        }
    }
}
