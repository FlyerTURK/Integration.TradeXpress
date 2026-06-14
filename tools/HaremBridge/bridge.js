// ─────────────────────────────────────────────────────────────────────────────
//  HaremBridge — Harem Altın canlı kur köprüsü
//
//  Harem'in canlipiyasalar.haremaltin.com sayfası Cloudflare bot-koruması
//  arkasında ve fiyatları socket.io WebSocket'i üzerinden PUSH ediyor; düz
//  HTTP istekleriyle (HttpClient/curl) çekilemiyor. Bu köprü, gerçek Chrome'u
//  Playwright ile başlatıp sayfanın kendi WebSocket trafiğini dinler ve
//  fiyatları localhost üzerinde sade bir JSON olarak sunar.
//
//  ERPPRO3 tarafı (HaremClient) bu JSON'u HTTP ile okur; kod-eşleme ve
//  TR-format parse C# tarafında yapılır (köprü "aptal" transport katmanıdır,
//  ham Harem sembollerini olduğu gibi geçer).
//
//  Çalıştırma:
//    İlk kurulum (challenge'ı bir kez elle çöz):  $env:HEADLESS='0'; node bridge.js
//    Servis modu (çerez saklandıktan sonra):       $env:HEADLESS='1'; node bridge.js
//    Windows Service olarak (NSSM):                 README.md'ye bakın.
//
//  Ortam değişkenleri:
//    HEADLESS   '1' → görünmez tarayıcı (varsayılan), '0' → görünür (challenge çözmek için)
//    PORT       HTTP endpoint portu (varsayılan 8765)
//    PROFILE    Kalıcı Chrome profil klasörü (varsayılan ./profile)
//
//  Endpoint:  GET http://localhost:<PORT>/
//    { "source":"haremaltin", "lastUpdate":"<ISO>", "ageSeconds":<n>,
//      "count":<n>, "kurlar": { "ALTIN":{alis,satis,tarih,...}, ... } }
// ─────────────────────────────────────────────────────────────────────────────

const { chromium } = require('playwright');
const http = require('http');

const PAGE_URL = 'https://canlipiyasalar.haremaltin.com/';
const HEADLESS = process.env.HEADLESS !== '0';        // varsayılan headless
const PORT = parseInt(process.env.PORT || '8765', 10);
const PROFILE = process.env.PROFILE || './profile';

// Watchdog: bu süre boyunca hiç fiyat paketi gelmezse sayfayı yeniden yükle
// (WebSocket kopması / Cloudflare challenge'ının yeniden gelmesi durumunu kurtarır).
const STALE_RELOAD_MS = 45_000;
const WATCHDOG_EVERY_MS = 15_000;

const kurlar = {};            // { ALTIN:{alis,satis,tarih,...}, USDTRY:{...}, ... }
let lastUpdate = null;        // Date — son fiyat paketi zamanı
let lastFrameAt = Date.now(); // Date.now() — son WS frame zamanı (watchdog için)
let reloading = false;

function log(level, msg) {
  // ISO zaman + seviye; NSSM/servis log dosyasına düşer.
  console.log(`${new Date().toISOString()} [${level}] ${msg}`);
}

/** socket.io frame'ini ayrıştırır; fiyat verisi içeren paketlerde cache'i günceller. */
function handleFrame(payload) {
  lastFrameAt = Date.now();
  const data = typeof payload === 'string' ? payload : payload.toString('utf8');
  // socket.io event paketi formatı: 42["event_adi", {...}]
  if (!data.startsWith('42')) return;
  let parsed;
  try { parsed = JSON.parse(data.slice(2)); } catch { return; }
  const body = parsed[1];
  // Fiyat verisi genelde { data: { ALTIN:{...}, USDTRY:{...} } } şeklinde gelir.
  const list = body && typeof body === 'object' ? (body.data ?? body) : null;
  if (!list || typeof list !== 'object') return;

  let count = 0;
  for (const [kod, v] of Object.entries(list)) {
    if (v && typeof v === 'object' && ('satis' in v || 'alis' in v)) {
      kurlar[kod] = v;
      count++;
    }
  }
  if (count > 0) lastUpdate = new Date();
}

async function clearChallenge(page) {
  for (let i = 0; i < 30; i++) {
    const title = await page.title().catch(() => '');
    if (!/just a moment|bir dakika|lütfen/i.test(title)) return true;
    if (i === 0) log('INFO', 'Cloudflare challenge bekleniyor...');
    await page.waitForTimeout(2000);
  }
  return false;
}

(async () => {
  log('INFO', `HaremBridge baslatiliyor (headless=${HEADLESS}, port=${PORT})`);

  const ctx = await chromium.launchPersistentContext(PROFILE, {
    headless: HEADLESS,
    channel: 'chrome',                                  // gerçek Chrome (stealth için şart)
    viewport: null,
    ignoreDefaultArgs: ['--enable-automation'],
    args: ['--disable-blink-features=AutomationControlled', '--start-maximized'],
  });
  const page = ctx.pages()[0] ?? await ctx.newPage();

  // websocket handler page üzerinde; her reload sonrası yeni WS'e otomatik bağlanır.
  page.on('websocket', ws => {
    log('INFO', `WebSocket baglandi: ${ws.url()}`);
    ws.on('framereceived', f => handleFrame(f.payload));
  });

  log('INFO', 'Sayfa aciliyor...');
  await page.goto(PAGE_URL, { waitUntil: 'domcontentloaded', timeout: 60_000 });
  const ok = await clearChallenge(page);
  log(ok ? 'INFO' : 'WARN',
    ok ? `Sayfa hazir: ${await page.title()}`
       : 'Challenge cozulemedi — gorunur modda (HEADLESS=0) bir kez elle cozun.');

  // ── Watchdog: veri akışı durursa sayfayı yenile ──────────────────────────
  setInterval(async () => {
    if (reloading) return;
    if (Date.now() - lastFrameAt < STALE_RELOAD_MS) return;
    reloading = true;
    try {
      log('WARN', `${STALE_RELOAD_MS / 1000}sn veri yok — sayfa yenileniyor`);
      await page.reload({ waitUntil: 'domcontentloaded', timeout: 60_000 });
      const r = await clearChallenge(page);
      if (!r) log('WARN', 'Yenileme sonrasi challenge cozulemedi — HEADLESS=0 ile elle cozun.');
      lastFrameAt = Date.now();   // reload denendi; bir sonraki turu hemen tetikleme
    } catch (e) {
      log('ERROR', `Reload hatasi: ${e.message}`);
    } finally {
      reloading = false;
    }
  }, WATCHDOG_EVERY_MS);

  // ── HTTP endpoint ────────────────────────────────────────────────────────
  http.createServer((req, res) => {
    const ageSeconds = lastUpdate ? Math.round((Date.now() - lastUpdate.getTime()) / 1000) : null;
    res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify({
      source: 'haremaltin',
      lastUpdate: lastUpdate ? lastUpdate.toISOString() : null,
      ageSeconds,
      count: Object.keys(kurlar).length,
      kurlar,
    }));
  }).listen(PORT, '127.0.0.1', () => log('INFO', `Endpoint hazir: http://127.0.0.1:${PORT}/`));

  // Temiz kapanış
  for (const sig of ['SIGINT', 'SIGTERM']) {
    process.on(sig, async () => { log('INFO', `${sig} — kapatiliyor`); await ctx.close().catch(() => {}); process.exit(0); });
  }
})().catch(e => { log('ERROR', `Fatal: ${e.stack || e.message}`); process.exit(1); });
