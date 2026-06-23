using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Financials.ExchangeRates;
using Microsoft.Playwright;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Scraping.N11;

/// <summary>
/// n11 kategori kazıyıcı + kâr motoru (TEST/demo). n11 Cloudflare WAF sert → gerçek Chrome + headed ile geçilir.
/// <c>a.product-item</c> kartlarından ad/fiyat/link/görsel çıkarılır; sonra her ürün için CANLI HAS spot
/// (<see cref="ExchangeRateCacheService"/>) + maliyet modeliyle Maliyet/Kâr/Kâr%/İndirim% hesaplanır.
/// </summary>
public sealed class N11Scraper : IN11Scraper, ITransientDependency
{
    // ── Maliyet modeli (TEST varsayımları; gerçek hayatta parametrik/AI olur) ──
    private const decimal Zarf = 5m;
    private const decimal Kargo = 117.84m;
    private const decimal SigortaRate = 0.0025m;   // binde 2,5 (beyan = has karşılığı)
    private const decimal KomisyonRate = 0.0593m;  // n11: %5,09 + %0,17 + %0,67 (külçe altın KDV %0)
    private const double  LaborFactor = 1.002;      // işçilik ≈ %0,2 (1g 995 örneğinden; ürün tipine göre değişir)
    private const decimal HasFallback = 6224.26m;   // feed boşsa

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ExchangeRateCacheService _cache;

    public N11Scraper(ExchangeRateCacheService cache) => _cache = cache;

    private const string ExtractorJs = @"() => {
  const cards = [...document.querySelectorAll('a.product-item')];
  const priceRe = /([0-9][0-9.]*,[0-9]{2})/g;
  const items = cards.map(card => {
    const img = card.querySelector('img');
    const name = (img?.getAttribute('alt') || card.querySelector('[title]')?.getAttribute('title') || '').trim();
    const text = (card.textContent || '').replace(/\s+/g, ' ').trim();
    const prices = [...text.matchAll(priceRe)].map(m => m[1]);
    const reviews = (text.match(/\((\d+)\)/) || [])[1] || null;
    return {
      name,
      url: card.href,
      image: img?.getAttribute('src') || null,
      listPrice: prices[0] || null,
      cartPrice: (text.includes('SEPETTE') && prices[1]) ? prices[1] : null,
      reviewCount: reviews ? Number(reviews) : null,
      prodId: card.getAttribute('data-prod-id'),
    };
  });
  return JSON.stringify({ items });
}";

    public async Task<List<N11Product>> GetCategoryAsync(string categoryUrl, int maxItems = 40, CancellationToken cancellationToken = default)
    {
        var profile = Path.Combine(AppContext.BaseDirectory, "n11-scrape-profile");

        using var playwright = await Playwright.CreateAsync();
        await using var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false,               // n11 WAF: headed gerçek Chrome şart
            Channel = "chrome",
            Locale = "tr-TR",
            TimezoneId = "Europe/Istanbul",
            IgnoreDefaultArgs = new[] { "--enable-automation" },
            Args = new[] { "--disable-blink-features=AutomationControlled", "--start-maximized" },
        });

        var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        await page.GotoAsync(categoryUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

        try { await page.WaitForSelectorAsync("a.product-item", new() { Timeout = 20_000 }); }
        catch { /* challenge/blok → boş döner */ }

        for (var i = 0; i < 4; i++)
        {
            await page.Mouse.WheelAsync(0, 2200);
            await page.WaitForTimeoutAsync(600);
        }

        var json = await page.EvaluateAsync<string>(ExtractorJs);
        var result = JsonSerializer.Deserialize<Wrapper>(json, JsonOptions);
        var items = (result?.Items ?? new List<N11Product>()).Take(maxItems).ToList();

        // Canlı HAS spot (satış/ask — satıcı altını bu fiyattan bastırır); feed boşsa fallback.
        var has = _cache.GetByCode(CurrencyUnitCode.HAS)?.Sell ?? 0m;
        if (has <= 0m) has = HasFallback;

        foreach (var p in items)
            Enrich(p, has);

        return items;
    }

    // ── Kâr motoru: başlık → ağırlık/milyem; canlı HAS + maliyet modeli → Maliyet/Kâr/Kâr%/İndirim% ──
    private static void Enrich(N11Product p, decimal hasSpot)
    {
        var cart = ParsePrice(p.CartPrice) ?? ParsePrice(p.ListPrice);
        var list = ParsePrice(p.ListPrice);
        if (list is > 0m && cart is > 0m && list > cart)
            p.DiscountPct = (double)((list.Value - cart.Value) / list.Value);

        var (grams, milyem) = ParseWeight(p.Name);
        if (grams is null || cart is null or <= 0m)
            return;

        p.WeightG = grams;
        p.Milyem = milyem;

        var hasEquiv = (decimal)(grams.Value * (milyem / 995.0) * LaborFactor);
        var fair = hasEquiv * hasSpot;                       // işçilik dahil has karşılığı
        var sigorta = fair * SigortaRate;
        var komisyon = cart.Value * KomisyonRate;
        var cost = fair + Zarf + Kargo + sigorta + komisyon;
        var profit = cart.Value - cost;

        p.FairValue = decimal.Round(fair, 2);
        p.TotalCost = decimal.Round(cost, 2);
        p.ProfitTl = decimal.Round(profit, 2);
        p.ProfitPct = cost > 0m ? (double)(profit / cost) : null;
    }

    // "7.238,53" → 7238.53
    private static decimal? ParsePrice(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var norm = s.Trim().Replace(".", string.Empty).Replace(',', '.');
        return decimal.TryParse(norm, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    // Başlıktan toplam gram + milyem (heuristik; üretimde LLM çıkarımı). "2 x 1 G", "2 Adet 1 Gr", "0.50 Gr"...
    private static (double? grams, int milyem) ParseWeight(string name)
    {
        var milyem = Regex.IsMatch(name, @"22\s*ayar|\b916\b", RegexOptions.IgnoreCase) ? 916 : 995;

        // "2 x 1 G" → adet × birim
        var x = Regex.Match(name, @"(\d+)\s*x\s*(\d+(?:[.,]\d+)?)\s*g", RegexOptions.IgnoreCase);
        if (x.Success && TryNum(x.Groups[2].Value, out var ux))
            return (int.Parse(x.Groups[1].Value) * ux, milyem);

        var pack = 1;
        var adet = Regex.Match(name, @"(\d+)\s*adet", RegexOptions.IgnoreCase);
        if (adet.Success) pack = int.Parse(adet.Groups[1].Value);

        // Birim gram: "gr"/"gram"/"g" öncesi sayı.
        var g = Regex.Match(name, @"(\d+(?:[.,]\d+)?)\s*g(?:r|ram)?\b", RegexOptions.IgnoreCase);
        if (g.Success && TryNum(g.Groups[1].Value, out var unit))
            return (pack * unit, milyem);

        return (null, milyem);
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out v);

    private sealed class Wrapper
    {
        public List<N11Product>? Items { get; set; }
    }
}
