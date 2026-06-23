using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Financials.ExchangeRates;

/// <summary>
/// In-process Harem canlı kur feed'i — harici HaremBridge (Node/Python/8765) GEREKMEZ. Microsoft.Playwright ile
/// (bundled Chromium, headless) canlipiyasalar.haremaltin.com'u açar, Cloudflare'i geçer (gerçek tarayıcı JS
/// challenge'ı çözer), sayfanın socket.io WebSocket'ini PASİF dinler ve gelen fiyat paketlerini
/// <see cref="HaremClient.MapQuotes"/> ile iç birim kodlarına çevirip <see cref="ExchangeRateCacheService"/>'e yazar.
/// Ayrıca 15-dk penceresinde host (TenantId=null) ham ExchangeRate satırını DB'ye persist eder.
///
/// <para><b>Tek sahip:</b> bu worker yalnız panoyu render eden host'ta (Blazor host) kayıtlıdır — cache aynı
/// process'tedir, pano in-process okur. Çok-instance'a geçerken IAbpDistributedLock ile tekilleştirilir.</para>
/// </summary>
public sealed class HaremPlaywrightFeedWorker : BackgroundWorkerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ExchangeRateOptions _options;
    private readonly ExchangeRateCacheService _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _lastFrameAtTicks;          // Interlocked — son fiyat paketi zamanı (watchdog)
    private DateTime _lastPersisted = DateTime.MinValue;

    public HaremPlaywrightFeedWorker(
        IOptions<ExchangeRateOptions> options,
        ExchangeRateCacheService cache,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await base.StartAsync(cancellationToken);

        if (!_options.HaremEnabled)
        {
            Logger.LogInformation("HaremPlaywrightFeedWorker devre dışı (HaremEnabled=false).");
            return;
        }

        _cts = new CancellationTokenSource();
        // Uzun-yaşam döngüsü ayrı Task'ta; StartAsync host başlatmayı bloklamaz.
        _loop = Task.Run(() => RunLoopAsync(_cts.Token));
        Logger.LogInformation("HaremPlaywrightFeedWorker başlatıldı (channel={Channel}, headless={Headless}).",
            _options.BrowserChannel ?? "bundled", _options.Headless);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_loop is not null)
            {
                try { await _loop.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
                catch { /* en iyi çaba ile kapan */ }
            }
            _cts.Dispose();
            _cts = null;
        }

        await base.StopAsync(cancellationToken);
    }

    // Tarayıcı kurulumu hata verirse backoff ile yeniden dene (challenge/ağ kurtarma).
    private async Task RunLoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(5);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunSessionAsync(ct);
                backoff = TimeSpan.FromSeconds(5); // temiz çıkış → backoff sıfırla
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Harem Playwright oturumu hata verdi; {Backoff}sn sonra yeniden denenecek.", backoff.TotalSeconds);
                try { await Task.Delay(backoff, ct); } catch { return; }
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60)); // üstel, 60sn tavan
            }
        }
    }

    private async Task RunSessionAsync(CancellationToken ct)
    {
        var profileDir = _options.BrowserProfileDir
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "harem-profile");

        using var playwright = await Playwright.CreateAsync();

        var launchOptions = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = _options.Headless,
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul",
            IgnoreDefaultArgs = new[] { "--enable-automation" },
            Args = new[] { "--disable-blink-features=AutomationControlled" },
        };
        if (!string.IsNullOrWhiteSpace(_options.BrowserChannel))
            launchOptions.Channel = _options.BrowserChannel;   // boş → bundled Chromium

        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(profileDir, launchOptions);
        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

        page.WebSocket += (_, ws) =>
        {
            Logger.LogInformation("Harem WS bağlandı: {Url}", ws.Url);
            ws.FrameReceived += (_, frame) => HandleFrame(frame.Text);
        };

        Interlocked.Exchange(ref _lastFrameAtTicks, DateTime.UtcNow.Ticks);
        await page.GotoAsync(_options.HaremPageUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
        await ClearChallengeAsync(page);

        // İç döngü: periyodik persist + watchdog (veri durursa sayfayı yenile).
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5_000, ct);

            await PersistIfWindowElapsedAsync(ct);

            var sinceLastFrame = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastFrameAtTicks), DateTimeKind.Utc);
            if (sinceLastFrame > _options.StaleReload)
            {
                Logger.LogWarning("{Sec}sn veri yok — Harem sayfası yenileniyor.", (int)sinceLastFrame.TotalSeconds);
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
                await ClearChallengeAsync(page);
                Interlocked.Exchange(ref _lastFrameAtTicks, DateTime.UtcNow.Ticks);
            }
        }
    }

    private static async Task ClearChallengeAsync(IPage page)
    {
        for (var i = 0; i < 30; i++)
        {
            var title = await page.TitleAsync();
            if (!System.Text.RegularExpressions.Regex.IsMatch(title, "just a moment|bir dakika|lütfen|attention",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return;
            await page.WaitForTimeoutAsync(2000);
        }
    }

    // socket.io event paketi: 42["event",{ data:{ ALTIN:{alis,satis,tarih}, ... } }] (veya doğrudan {SYMBOL:{...}}).
    private void HandleFrame(string? data)
    {
        if (string.IsNullOrEmpty(data) || !data.StartsWith("42", StringComparison.Ordinal)) return;

        Dictionary<string, HaremClient.RawQuote>? raw;
        try
        {
            using var doc = JsonDocument.Parse(data[2..]);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2) return;
            var body = arr[1];
            var listEl = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("data", out var d) ? d : body;
            if (listEl.ValueKind != JsonValueKind.Object) return;
            raw = JsonSerializer.Deserialize<Dictionary<string, HaremClient.RawQuote>>(listEl.GetRawText(), JsonOptions);
        }
        catch
        {
            return; // fiyat dışı/biçimsiz frame
        }

        if (raw is null || raw.Count == 0) return;

        // HaremClient'ın eşleme + PLT/PLD türetme mantığını AYNEN kullan (delta paketlerde eksik sembol sorun değil).
        var quotes = HaremClient.MapQuotes(raw);
        // Canlı cache LENIENT: pozitif fiyatlı tüm mapped kotasyonlar (freshness pano için uygulanmaz; persist'te uygulanır).
        var live = quotes.Where(q => q.Buy > 0m && q.Sell > 0m).ToList();
        if (live.Count == 0) return;

        _cache.UpdatePrices(live);
        Interlocked.Exchange(ref _lastFrameAtTicks, DateTime.UtcNow.Ticks);
    }

    // 15-dk penceresinde bir kez host ham snapshot'ını DB'ye yazar (mevcut SnapshotWriter; PersistedCodes + freshness).
    private async Task PersistIfWindowElapsedAsync(CancellationToken ct)
    {
        var rounded = RoundDown(DateTime.UtcNow, _options.PersistInterval);
        if (rounded <= _lastPersisted) return;

        var freshnessFloor = DateTime.UtcNow - _options.HaremFreshness;
        var snapshot = _cache.GetAll().Values
            .Where(q => q.Buy > 0m && q.Sell > 0m
                        && _options.PersistedCodes.Contains(q.Code)
                        && (q.UpdatedAtUtc is null || q.UpdatedAtUtc >= freshnessFloor))
            .ToList();
        if (snapshot.Count == 0) return;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var uowManager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
            using var uow = uowManager.Begin(requiresNew: true);
            var writer = scope.ServiceProvider.GetRequiredService<ExchangeRateSnapshotWriter>();
            var written = await writer.WriteAsync(rounded, snapshot, ct);
            await uow.CompleteAsync(ct);
            _lastPersisted = rounded;
            Logger.LogInformation("ExchangeRate snapshot persisted for window {RateDate:u} ({Count} rows).", rounded, written);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ExchangeRate snapshot persist başarısız (window {RateDate:u}).", rounded);
        }
    }

    private static DateTime RoundDown(DateTime value, TimeSpan interval)
    {
        var ticks = value.Ticks - value.Ticks % interval.Ticks;
        return new DateTime(ticks, value.Kind);
    }
}
