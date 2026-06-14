# HaremBridge

Harem Altın canlı kurlarını ERPPRO3'e taşıyan köprü.

Harem'in `canlipiyasalar.haremaltin.com` sayfası **Cloudflare managed-challenge**
arkasında ve fiyatları **socket.io v4 WebSocket**'i üzerinden push ediyor
(`wss://hrmsocketonly.haremaltin.com/socket.io/?EIO=4&transport=websocket`);
düz HTTP ile (`HttpClient`/`curl`) çekilemiyor, headless tarayıcı da challenge'ı
geçemiyor. Çalışan mimari iki parçalı:

1. **harvest.js** — KISA süreliğine **headed gerçek Chrome** açar, Cloudflare'ı
   geçer, `cf_clearance` çerezini + User-Agent'ı `harvest.json`'a yazar, kapanır.
   (`cf_clearance` domain=`.haremaltin.com` → socket host dahil tüm subdomainler.)
2. **bridge.py** — Python `curl_cffi` (`impersonate='chrome'` = Chrome TLS/JA3
   parmak izi) + harvest edilen çerez ile **TEK kalıcı WebSocket** tutar.
   Steady-state'te tarayıcı KAPALIDIR. Gelen fiyat paketlerini belleğe yazıp
   `http://127.0.0.1:8765/` adresinde JSON sunar.

```
[harvest.js: kısa headed Chrome] --bir kez--> cf_clearance --> harvest.json
                                                                 |
[bridge.py: curl_cffi tek kalıcı WS] <--- okur -------------------+
        │ wss socket.io v4 (TEXT frame zorunlu)
        ▼
   http://127.0.0.1:8765/         (GET  — anlık JSON snapshot, poll/teşhis)
   http://127.0.0.1:8765/events   (SSE  — push: ilk event snapshot, sonrası delta)
        ▼ push (SSE)
[ERPPRO3 HttpApi.Host]
      ├─ HaremPushListener  (SSE; her price_changed'de cache'i ANINDA günceller)
      └─ ExchangeRateBackgroundWorker
           ├─ Harem durumu  (listener'dan; push kesilirse GET poll fallback'i)
           └─ AltinkaynakClient (fallback + RUB/AZN/CNY/RON/AED + JPY/KWD)
```

> *** KRİTİK İNCELİK ***: `curl_cffi` `ws.send()` varsayılan olarak BINARY frame
> yollar; socket.io v4 string paketleri (`0`/`40`/`42`) **TEXT frame** zorunlu
> kılar. TEXT gönderilmezse sunucu `40` sonrası bağlantıyı kapatır. Çözüm:
> `flags=CurlWsFlag.TEXT`.

> `bridge.js` önceki (Node/Playwright, tarayıcıyı sürekli açık tutan) iterasyondur;
> referans olarak duruyor, kullanılmıyor.

## Gereksinimler

- Node.js + **Google Chrome** (Playwright'ın kendi Chromium'u DEĞİL — gerçek
  Chrome şart; Chromium challenge'ı geçemiyor).
- Python 3.10+ ; `pip install curl_cffi` (>= 0.15).

```powershell
cd E:\Kodlarim\ERPPROV3\tools\HaremBridge
npm install          # playwright (harvest.js için)
pip install curl_cffi
```

## Çalıştırma

```powershell
python bridge.py     # PORT env ile port değiştirilebilir (vars. 8765)
```

- İlk açılışta (ve `cf_clearance` dolunca / 403 gelince) bridge otomatik
  `node harvest.js` tetikler → birkaç saniye **görünür Chrome** açılır,
  challenge geçer, kapanır. Gerisi tarayıcısız.
- **Headed harvest interaktif masaüstü oturumu ister** — headless ÇALIŞMAZ.

### Dayanıklılık (bridge.py içinde)

- **Watchdog:** 55 sn hiç frame gelmezse ölü bağlantı, 120 sn fiyat gelmezse
  bayat → WS kapatılır, 3 sn sonra reconnect.
- `cf_clearance` proaktif yenileme: `HARVEST_INTERVAL` = 2 saat (tahmini ömür;
  403 sıklığına göre ayarlanmalı). 403/401/503'te `harvest.json` silinip
  yeniden harvest edilir; 429'da retry 30 sn'ye yavaşlar (socket host
  rate-limit'li — **tek kalıcı WS** kullan, paralel bağlantı açma).
- Köprü tamamen düşse bile ERPPRO3 etkilenmez: `HaremFreshness` (vars. 2 dk)
  içinde taze veri gelmeyince worker **Altınkaynak fallback**'ine düşer.

## Windows'ta sürekli çalıştırma

Headed harvest **interaktif masaüstü oturumu** ister; Windows servisleri
Session 0'da koştuğu için düz servis olmaz. Önerilen yol:

- **Task Scheduler — "At log on"** (ilgili kullanıcı).
  - Action: `python.exe E:\Kodlarim\ERPPROV3\tools\HaremBridge\bridge.py`
    (Start in: `E:\Kodlarim\ERPPROV3\tools\HaremBridge`)
  - "Run only when user is logged on" seçili; "Restart on failure" ekli.
  - Makinede oturum açık kalmalı (otomatik-logon + kilitli konsol veya kalıcı
    RDP/konsol oturumu).

## ERPPRO3 tarafı ayarları

`ERPPRO3.HttpApi.Host/appsettings.json` → `ExchangeRates`:

```jsonc
"HaremEnabled":   true,                      // false → yalnız Altınkaynak
"HaremBridgeUrl": "http://127.0.0.1:8765/",
"HaremHttpTimeout": "00:00:02",
"HaremFreshness":   "00:02:00"               // bu yaşı aşan Harem kotasyonu fallback'e bırakılır
```

## Endpoint'ler

### `GET /events` — SSE push kanalı (ERP'nin kullandığı yol)

`text/event-stream`; bağlanan istemciye İLK event olarak tam snapshot, sonrasında
her `price_changed`'de yalnız DEĞİŞEN semboller (delta) gönderilir — her iki
event de aynı `{"lastUpdate","kurlar":{...}}` şemasını taşır, tüketici merge
eder. Sessiz piyasada ≤15 sn'de bir `: ping` yorumu akar (canlılık);
ERP tarafı `HaremStreamIdleTimeout` (45 sn) hiç satır gelmezse yeniden bağlanır.
Sunucu `ThreadingHTTPServer` — çoklu eşzamanlı abone desteklenir.

### `GET /` — anlık JSON snapshot (poll fallback + teşhis)

```jsonc
{
  "source": "haremaltin",
  "lastUpdate": "2026-06-04T14:46:08+00:00",
  "ageSeconds": 1,
  "stale": false,          // ageSeconds > 120 → true; tüketici fallback uygular
  "count": 58,
  "kurlar": {
    "ALTIN":    { "alis": "6613.180", "satis": "6641.360", "tarih": "04-06-2026 17:46:11" },
    "USDTRY":   { "alis": "45.9160",  "satis": "45.9910",  "tarih": "04-06-2026 17:45:40" },
    "GUMUSTRY": { "alis": "104,535",  "satis": "112,144",  "tarih": "04-06-2026 17:46:11" }
  }
}
```

Veri detayları (tüketici tarafı):

- ~58 sembol akar; ham Harem sembolleri AYNEN geçer (eşleme `HaremCodeMapping`'te).
- Sayı formatı sembole göre nokta veya virgül ondalık (`"45.9160"` / `"104,535"`);
  **binlik ayraç kullanılmaz**. Parse: virgülü noktaya çevir, InvariantCulture.
- **Bazı semboller (örn. `OMRUSD`) `alis`/`satis`'i JSON *number* olarak push
  eder** — tüketici string VE number token'ı kabul etmeli (`HaremClient`'taki
  `LenientStringConverter` emsali).
- `tarih`: `dd-MM-yyyy HH:mm:ss`, Türkiye yerel saati (UTC+3, DST yok).
- Harem ŞU dövizleri VERMEZ: RUB, AZN, CNY, RON, AED (Altınkaynak'tan gelir).
  KWD bayat gelebilir (freshness guard'ı yakalar).
- PLATİN/PALADYUM: `PLATIN`/`PALADYUM` kolonları **USD/kilogram** cinsindendir
  (kanıt: aynı konvansiyonda `USDKG×USDTRY/1000 ≈ KULCEALTIN` ve
  `GUMUSUSD×USDTRY/1000 ≈ GUMUSTRY` birebir tutar). PLT/PLD gram-TRY türetimi
  `HaremClient`'ta: `alis/1000×USDTRY.alis`, `satis/1000×USDTRY.satis`.
  Spread perakende geniştir (Pt ~%9, Pd ~%30) — kaynak seçimi 2026-06-04
  kullanıcı kararı (eski Swissquote+Stooq harici feed'i kaldırıldı; PLT/PLD'nin
  Altınkaynak fallback'i yoktur, Harem kesilirse son cache değeri sunulur).

## Bilinen uyarılar

- `cf_clearance` ömrü kesin ölçülmedi; `HARVEST_INTERVAL`=2sa tahmin. İlk hafta
  403 sıklığını logla, aralığı ona göre ayarla.
- Resmi olmayan yöntem; Harem site/koruması değişirse kırılabilir. Resmi
  alternatif: altinapi.com (ücretli, REST+socket.io, tarayıcısız).
