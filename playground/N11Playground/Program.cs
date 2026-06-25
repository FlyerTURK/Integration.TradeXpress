using Microsoft.Playwright;

// ─────────────────────────────────────────────────────────────────────────────
//  N11Playground — n11 kategori ürünlerini çekme (Harem playground'u ile AYNI izole
//  Playwright/Chromium). n11 Cloudflare WAF sert → gerçek Chrome + headed şart.
//  Kart = a.product-item; fiyat KARTA scope'lanır (ortak ata'dan değil).
//  Çalıştırma: dotnet run     |  URL=<kategori> CHANNEL=chrome HEADLESS=0 dotnet run
// ─────────────────────────────────────────────────────────────────────────────

var url = Environment.GetEnvironmentVariable("URL") ?? "https://www.n11.com/altin-ve-gumus/kulce-altin";
bool headless = Environment.GetEnvironmentVariable("HEADLESS") == "1";          // n11 için varsayılan headed
var channel = Environment.GetEnvironmentVariable("CHANNEL") ?? "chrome";        // chrome | msedge | bundled
var profile = Path.Combine(AppContext.BaseDirectory, "n11-profile");

Console.WriteLine($"[n11] açılıyor: {url} (channel={channel}, headless={headless})");

using var pw = await Playwright.CreateAsync();
var opts = new BrowserTypeLaunchPersistentContextOptions
{
    Headless   = headless,
    Locale     = "tr-TR",
    TimezoneId = "Europe/Istanbul",
    IgnoreDefaultArgs = new[] { "--enable-automation" },
    Args       = new[] { "--disable-blink-features=AutomationControlled", "--start-maximized" },
};
if (channel != "bundled") opts.Channel = channel;
await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profile, opts);

var page = ctx.Pages.FirstOrDefault() ?? await ctx.NewPageAsync();
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });
Console.WriteLine($"[n11] title: {await page.TitleAsync()}");

try { await page.WaitForSelectorAsync("a.product-item", new() { Timeout = 20_000 }); }
catch { Console.WriteLine("[n11] a.product-item bulunamadı (challenge/blok?)."); }

// Lazy-load için kaydır.
for (int i = 0; i < 4; i++) { await page.Mouse.WheelAsync(0, 2200); await page.WaitForTimeoutAsync(600); }

var json = await page.EvaluateAsync<string>(@"() => {
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
  return JSON.stringify({ title: document.title, url: location.href, count: items.length, items: items.slice(0, 40) });
}");

Console.WriteLine("[n11] === SONUC ===");
Console.WriteLine(json);
