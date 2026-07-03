# TradeXpress — Operasyon & Uzun-Ömür Kılavuzu

Bu doküman 2026-07-03 uzun-ömür denetiminin **kalıcı çözümlerini** ve **temiz-makine kurulumunu** belgeler.
Amaç: 5-10 yıl sonra bile bu koddan pişman olmamak.

## 1. Temiz makinede kurulum (5-10 yıl sonra da)
1. **.NET SDK 10.0.3xx** (bkz. `global.json`; `rollForward: latestMinor` → 10.x serisi). SDK yoksa build reddeder (deterministik).
2. **DevExpress** kurulumu GEREKMEZ — gerekli 9 Blazor paketi `nuget-packages/`'te vendored (repo NuGet.Config local source). Yeni DevExpress paketi eklenirse `etc/scripts/vendor-devexpress.ps1` çalıştır.
3. `dotnet tool restore` (dotnet-ef sürümü `.config/dotnet-tools.json`'da pinli).
4. `dotnet restore` → `packages.lock.json`'lar deterministik transitive graf verir.
5. **Secret'lar:** her host'ta `appsettings.secrets.json.example`'ı `appsettings.secrets.json` olarak kopyala + GERÇEK değerleri gir (repoda yok — güvenlik).
6. **Sertifikalar:** `certs/` (Kestrel TLS) + `openiddict.pfx` repoda YOK (gitignore) — ayrıca sağlanmalı (bkz. §3).
7. `dotnet build` → `dotnet run` (Blazor host :44318).

## 2. Secret yönetimi (K3/K4)
- **GERÇEK secret'lar yalnız `appsettings.secrets.json`'da** (gitignore'lu). `appsettings.json`'a secret YAZMA.
- **Rotasyon gerekenler** (git geçmişinde açıkta kaldılar — DEĞİŞTİR):
  - SQL parolası (`sa` yerine sınırlı yetkili app login'i aç)
  - `AuthServer:CertificatePassPhrase` (yeni cert ile birlikte)
  - `StringEncryption:DefaultPassPhrase`
- **GitHub PAT** (`NuGet.Config`'ten kaldırıldı) → GitHub'da **revoke et** (geçmişte açıkta).
- **Git geçmişi temizliği:** yukarıdakiler eski commit'lerde duruyor → `git filter-repo`/BFG ile geçmişten sil + force-push (ekip azsa uygun). Rotasyon yapıldıysa aciliyet düşer.

## 3. Sertifika rotasyonu (K1/K2 — zamanlı bombalar)
- **OpenIddict (`openiddict.pfx`) — 2027-06-11'de doluyor.** Dolunca tüm login/token çöker.
  → `etc/scripts/new-openiddict-cert.ps1 -PassPhrase "<yeni>" -Years 10` ile 10 yıllık üret; pfx'i HttpApi.Host + Blazor altına kopyala; yeni parolayı secrets.json'a yaz. (Bir kez herkes yeniden login olur.)
- **Kestrel TLS (`certs/*.crt`) — ~90 günde bir doluyor** (Let's Encrypt/Tailscale). Otomatik yenileme zinciri kurulmalı (Tailscale cert / certbot → `certs/` güncelle → host restart). Mutlak `E:\` yolu yerine relatif/ortam değişkeni tercih et.

## 4. Yedekleme (Y2)
- `etc/scripts/backup-database.ps1` — SQLEXPRESS'te SQL Agent yok → **Windows Task Scheduler** ile günlük (script başında kurulum komutu). FULL + CHECKSUM + RESTORE VERIFYONLY + retention.
- Ek: FULL recovery model + 15dk LOG yedeği + **off-site kopya** + **aylık gerçek restore testi**.

## 5. Makineye-bağlı config (Y4)
- `umut.taile7a850.ts.net` (Tailscale) + `E:\Kodlarim\Yeni\certs\...` mutlak yollar `appsettings.json`'larda gömülü.
- Taşınabilirlik için: kalıcı domain + ortam değişkeni/secrets.json'a taşı; `appsettings.json`'da yalnız placeholder.

## 6. Bağımlılık bakımı
- Sürümler `Directory.Packages.props`'ta merkezi + sabit; `packages.lock.json` deterministik.
- **Transitive HIGH açıklar** (Crypto.Xml/OpenApi/MessagePack/SQLite): Microsoft.* 10.0.9'a çekildi. Kalanlar için ABP 10.4.1→10.5.0 (test ile) bump'ı önerilir.
- DevExpress 25.2.5 **bilinçli sabit** (26.x'e bakma). net10 = LTS (~2028 destek sonu) — 2028 öncesi bir sonraki LTS'e plan.

## 7. Mekanik governance ağı (armlı)
- Derleme: BannedApi (Guid.NewGuid/ham exception/Check.NotNullOrWhiteSpace) + expression-bodied (Domain) = HATA.
- Test: EntityConvention/RazorConvention/LocalizationParity/Navigation/AppServiceConvention. `dotnet test` yeşil olmalı.
