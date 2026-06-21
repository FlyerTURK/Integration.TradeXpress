using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Threading;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// Harem (HaremBridge) feed'ini <see cref="ExchangeRateOptions.FetchInterval"/>'da poll eder,
/// bellek cache'ini günceller ve <see cref="ExchangeRateOptions.PersistInterval"/> (15 dk)
/// penceresinde bir kez <b>host</b> (TenantId=null) ham piyasa satırını DB'ye yazar.
///
/// <para><b>Yalnız host</b> ExchangeRate yazar; margin per-tenant olduğundan satır
/// HAM piyasa fiyatı tutar (AppliedMargin=Passthrough). Tenant'lar bu satırları okuma
/// anında kendi marjlarıyla görür (sonraki increment).</para>
///
/// <para>v1 polling (SSE push sonra). Altınkaynak fallback YOK — Harem kesilirse cache'teki
/// son değer korunur, yeni satır yazılmaz. Köprü kapalıysa worker no-op.</para>
/// </summary>
public class ExchangeRateFeedWorker : AsyncPeriodicBackgroundWorkerBase
{
    private readonly ExchangeRateOptions _options;
    private DateTime _lastPersisted = DateTime.MinValue;
    private int _running = 0;
    private int _consecutiveErrors = 0;

    public ExchangeRateFeedWorker(
        AbpAsyncTimer timer,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<ExchangeRateOptions> options)
        : base(timer, serviceScopeFactory)
    {
        _options = options.Value;
        Timer.Period = (int)_options.FetchInterval.TotalMilliseconds;
        // Host açılır açılmaz ilk fetch+persist (5 sn beklemeden). İlk tick'te _lastPersisted=MinValue
        // olduğundan snapshot hemen yazılır; RateDate = mevcut 15 dk penceresi (ör. 15:29 → 15:15).
        Timer.RunOnStart = true;
    }

    [UnitOfWork]
    protected override async Task DoWorkAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        if (!_options.HaremEnabled)
            return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return; // önceki tick hâlâ çalışıyor

        try
        {
            await DoWorkCoreAsync(workerContext);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task DoWorkCoreAsync(PeriodicBackgroundWorkerContext workerContext)
    {
        var ct = workerContext.CancellationToken;
        var sp = workerContext.ServiceProvider;

        var harem  = sp.GetRequiredService<HaremClient>();
        var cache  = sp.GetRequiredService<ExchangeRateCacheService>();
        var logger = sp.GetRequiredService<ILogger<ExchangeRateFeedWorker>>();

        // ── Fetch (poll) ──
        List<MarketQuote> quotes;
        try
        {
            quotes = await harem.FetchAllAsync(ct);
            RegisterRecovered(logger);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            RegisterFailure(ex, logger);
            return; // köprü erişilemez → cache/DB son durumu korunur
        }

        // Taze + sıfırdan büyük + bizim kodlarımız.
        var freshnessFloor = DateTime.UtcNow - _options.HaremFreshness;
        var fresh = quotes.Where(q =>
                q.Buy > 0m && q.Sell > 0m &&
                q.UpdatedAtUtc is not null && q.UpdatedAtUtc >= freshnessFloor &&
                _options.PersistedCodes.Contains(q.Code))
            .ToList();

        if (fresh.Count == 0)
            return;

        cache.UpdatePrices(fresh);

        // ── Persist (15 dk penceresi, yalnız host) ──
        var rounded = RoundDown(DateTime.UtcNow, _options.PersistInterval);
        if (rounded <= _lastPersisted)
            return;

        try
        {
            var writer = sp.GetRequiredService<ExchangeRateSnapshotWriter>();
            var written = await writer.WriteAsync(rounded, cache.GetAll().Values.ToList(), ct);
            _lastPersisted = rounded;
            logger.LogInformation("ExchangeRate snapshot persisted for window {RateDate:u} ({Count} rows)", rounded, written);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist ExchangeRate snapshot for window {RateDate:u}", rounded);
        }
    }

    private void RegisterFailure(Exception ex, ILogger logger)
    {
        _consecutiveErrors++;
        if (_consecutiveErrors == 1 || _consecutiveErrors % _options.LogEveryNthFailure == 0)
        {
            var level = ex is HttpRequestException or TaskCanceledException ? LogLevel.Warning : LogLevel.Error;
            logger.Log(level, ex,
                "Harem feed unreachable (consecutive failure #{Count}). Last cached data is served.",
                _consecutiveErrors);
        }
    }

    private void RegisterRecovered(ILogger logger)
    {
        if (_consecutiveErrors == 0) return;
        logger.LogInformation("Harem feed recovered after {Count} consecutive error(s)", _consecutiveErrors);
        _consecutiveErrors = 0;
    }

    private static DateTime RoundDown(DateTime value, TimeSpan interval)
    {
        var ticks = value.Ticks - value.Ticks % interval.Ticks;
        return new DateTime(ticks, value.Kind);
    }
}
