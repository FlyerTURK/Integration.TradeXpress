const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const ctx = await chromium.launchPersistentContext('./profile', {
    headless: false, channel: 'chrome', viewport: null,
    ignoreDefaultArgs: ['--enable-automation'],
    args: ['--disable-blink-features=AutomationControlled', '--start-maximized'],
  });
  const page = ctx.pages()[0] ?? await ctx.newPage();
  await page.goto('https://canlipiyasalar.haremaltin.com/', { waitUntil: 'domcontentloaded', timeout: 60000 });
  // Challenge bekleme: CF challenge gecisi sayfayi yeniden navigate eder;
  // o anda title() patlar/bos doner — bu "henuz oturmadi" demektir, beklemeye
  // devam et (eski surum burada erken break edip evaluate'i navigation'a
  // denk getiriyordu -> "Execution context was destroyed").
  for (let i = 0; i < 30; i++) {
    let t = null;
    try { t = await page.title(); } catch { /* navigation aninda */ }
    if (t && !/just a moment|bir dakika|lütfen/i.test(t)) break;
    await page.waitForTimeout(2000);
  }
  // evaluate hala bir navigation'a denk gelebilir — kisa araliklarla dene.
  let ua = '';
  for (let i = 0; i < 10; i++) {
    try { ua = await page.evaluate('navigator.userAgent'); break; }
    catch { await page.waitForTimeout(1000); }
  }
  if (!ua) throw new Error('navigator.userAgent okunamadi (surekli navigation)');
  const cookies = await ctx.cookies();
  const cf = cookies.find(c => c.name === 'cf_clearance');
  fs.writeFileSync('harvest.json', JSON.stringify({
    ua, cf_clearance: cf ? cf.value : null,
    cookieHeader: cookies.map(c => `${c.name}=${c.value}`).join('; '),
  }, null, 2));
  console.log('cf_clearance:', cf ? 'ALINDI' : 'YOK');
  await ctx.close();
  process.exit(0);
})().catch(e => { console.log('HATA:', e.message); process.exit(1); });
