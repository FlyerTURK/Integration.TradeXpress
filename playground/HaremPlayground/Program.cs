using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

// ─────────────────────────────────────────────────────────────────────────────
//  HaremPlayground — C# Microsoft.Playwright ile Harem Cloudflare bypass + socket.io
//  okuma denemesi. Amaç: bridge.js mantığının .NET'te birebir çalıştığını kanıtlamak.
//  Çalıştırma: dotnet run  (görünür Chrome açılır; JS challenge gerçek tarayıcıda çözülür)
// ─────────────────────────────────────────────────────────────────────────────

const string url = "https://canlipiyasalar.haremaltin.com/";
bool headless = Environment.GetEnvironmentVariable("HEADLESS") == "1";
// CHANNEL: "chrome" (varsayılan) | "msedge" | "bundled" (Playwright'ın kendi Chromium'u, channel yok)
var channel = Environment.GetEnvironmentVariable("CHANNEL") ?? "chrome";
// Her motor için ayrı profil → çerez/clearance karışmasın.
var profile = Path.Combine(AppContext.BaseDirectory, "profile-" + channel);

Console.WriteLine($"[playground] Playwright başlatılıyor (channel={channel}, headless={headless}, profile={profile})");

using var pw = await Playwright.CreateAsync();

var opts = new BrowserTypeLaunchPersistentContextOptions
{
    Headless   = headless,
    Locale     = "tr-TR",
    TimezoneId = "Europe/Istanbul",
    IgnoreDefaultArgs = new[] { "--enable-automation" },
    Args       = new[] { "--disable-blink-features=AutomationControlled", "--start-maximized" },
};
if (channel != "bundled") opts.Channel = channel;   // "bundled" → channel verilmez (Playwright Chromium)

await using var ctx = await pw.Chromium.LaunchPersistentContextAsync(profile, opts);

var page = ctx.Pages.FirstOrDefault() ?? await ctx.NewPageAsync();

int frames = 0;        // toplam fiyatlı sembol güncellemesi
int batches = 0;       // gelen fiyat PAKETİ (push) sayısı
var sample = new List<string>();
var seen = new HashSet<string>();
bool watch = Environment.GetEnvironmentVariable("WATCH") == "1";
string? lastUsd = null;

page.WebSocket += (_, ws) =>
{
    Console.WriteLine($"[playground] WS bağlandı: {ws.Url}");
    ws.FrameReceived += (_, f) =>
    {
        var data = f.Text ?? "";
        if (!data.StartsWith("42")) return;   // socket.io event paketi: 42["event",{...}]
        try
        {
            using var doc = JsonDocument.Parse(data[2..]);
            var arr = doc.RootElement;
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2) return;
            var body = arr[1];
            var list = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("data", out var d) ? d : body;
            if (list.ValueKind != JsonValueKind.Object) return;
            int inThisFrame = 0;
            string? usd = null;
            foreach (var p in list.EnumerateObject())
            {
                if (p.Value.ValueKind != JsonValueKind.Object) continue;
                var hasAlis  = p.Value.TryGetProperty("alis",  out var alisEl);
                var hasSatis = p.Value.TryGetProperty("satis", out var satisEl);
                if (!hasAlis && !hasSatis) continue;
                frames++;
                inThisFrame++;
                var alis  = hasAlis  ? alisEl.ToString()  : "?";
                var satis = hasSatis ? satisEl.ToString() : "?";
                if (p.Name == "USDTRY") usd = $"{alis}/{satis}";
                if (seen.Add(p.Name) && sample.Count < 8) sample.Add($"{p.Name} {alis}/{satis}");
            }
            if (inThisFrame > 0)
            {
                batches++;
                if (watch)
                {
                    var changed = usd != null && usd != lastUsd ? " *DEĞİŞTİ*" : "";
                    if (usd != null) lastUsd = usd;
                    Console.WriteLine($"[tick] {DateTime.Now:HH:mm:ss.fff}  paket#{batches}  {inThisFrame} sembol  USDTRY={usd ?? "-"}{changed}");
                }
            }
        }
        catch { /* fiyat dışı frame */ }
    };
};

Console.WriteLine("[playground] Sayfa açılıyor...");
await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60_000 });

// Cloudflare challenge bekle (title "Just a moment / Bir dakika" iken bekle).
bool cleared = false;
for (int i = 0; i < 30; i++)
{
    var title = await page.TitleAsync();
    if (!Regex.IsMatch(title, "just a moment|bir dakika|lütfen|attention", RegexOptions.IgnoreCase)) { cleared = true; break; }
    if (i == 0) Console.WriteLine("[playground] Cloudflare challenge bekleniyor...");
    await Task.Delay(2000);
}
var finalTitle = await page.TitleAsync();
Console.WriteLine($"[playground] challenge geçildi={cleared} title=\"{finalTitle}\"");

// İlk veri için bekle (max 30sn).
for (int i = 0; i < 30 && frames == 0; i++) await Task.Delay(1000);

// İzleme modu (WATCH=1): sürekli push akışını görmek için ek pencere boyunca dinle (varsayılan 60sn).
if (watch)
{
    int secs = int.TryParse(Environment.GetEnvironmentVariable("SECONDS"), out var s) ? s : 60;
    Console.WriteLine($"[playground] İzleme: {secs}sn boyunca canlı push akışı dinleniyor (her paket [tick] satırı)...");
    await Task.Delay(secs * 1000);
}

var verdict = cleared && frames > 0 ? "BAŞARILI" : cleared ? "CF-OK-ama-veri-yok" : "CF-GEÇİLEMEDİ";
Console.WriteLine($"[playground] RESULT verdict={verdict} cleared={cleared} paket(push)={batches} güncelleme={frames} sembol={seen.Count}");
Console.WriteLine($"[playground] SAMPLE {string.Join(" | ", sample)}");
