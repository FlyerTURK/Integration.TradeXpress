# Gece Raporu — 2026-07-24 (v2, UI turu + implementasyon)

> Hakan uyudu; talimat: commit → yeni implementasyonlar → commit+push → PC shutdown.
> Yarın direksiyon Hakan'da (taksi). Bu rapor commit'e girmez (kök .md, git dışı).

## Yapıldı & PUSH edildi (2 commit)

**Commit 1 — `7a2a55f`** (Dilim 1-3 muadil varyant + bu turun canlı düzeltmeleri):
- Product.VariantMode (Single/Multi/Substitution) + muadil varyant-ağacı + kombinasyon→reçete materyalizasyon
- taxHouse hotfix · NumericSpinEdit MinValue/MaxValue/NullText koşullu passthrough · 3 kanal reçete MetalVariants beslemesi · MetalCode/VariantName lokalizasyon

**Commit 2 — `08b9ec2`** (yenile penceresi #5/#6/#7):
- **#5** ToleranceType.Gram → **Amount (Miktar)** — birim-agnostik (madende gram, Good'da kg); "Miktar (±)"/"Binde (±)"; enum değeri 1 sabit, migration yok
- **#6** Muadil Product formu (7 madde): ilk grup otomatik+kalem yükle · grupsuzken alt kontroller gizli · grup clear yok · hedef 0-default · tolerans Miktar-default (devral kaldırıldı) · tolerans değeri yalnız Binde'de (Miktar'da gizli+sıfırla)
- **#7** Opsiyonel tanımlar (üretim/son kullanma tarihi) → drill öncesi collapsed grup

**Testler:** Domain 326/326 · Application 213/213 · EFCore 211/212
(tek kırık = `Host_should_have_seeded_country_catalog`, bilinen eski Country-seed borcu — task_fe0d89d1 ayrı oturumda; bu işle İLGİSİZ, yeni kırık YOK).

## Bilerek YAPILMADI (sabah, gözetim gerekli)

- **#3 Muadil varsayılan (boş=ana → hepsi materyalize):** çekirdek muadil davranışını çeviriyor + materyalize tetikleme inceliği (maden eklenince tüm varyant id'leri yazılır; yeni doğan girmez). Dilim 1-3 temeli — gözetimsiz bozma riski.
- **#1 Good emtiasını reçeteye entegre:** büyük dikey (server cost + client panel + 5 form + test). Not: Good birim seçeneği (StockUnitCode) ZATEN var.
- **#2 N11 GPSR gruplama:** iki-grid bölme (zorunlu + collapsed opsiyonel) + edit mantığı koruması; test ister.
- **#4 TÜM emtialar per-company + ICompanyOwned:** migration + veri taşıma + 43 kod insan-onayı. AYRI PLAN OTURUMU. (7/7 emtia ailesinde cross-company açık teşhis edildi; Metal 44 holding artık; çeyrek doğru=1.75gr×916milyem.)
- **#8 SpecialInfo+Personalization birleştir:** refactor + migration. AYRI PLAN OTURUMU. (char-limit Etsy-özel default 128.)

## Vizyon dosyası zenginleşti (roadmap DIŞI, salt vizyon)
Sübvansiyon-avı timing motoru · tedarik moat'ı · birim dönüşüm sistemi · (banka-transfer = ürün/vadeli-reçete, roadmap'ten ÇIKARILDI — web-sitesi kanalı gelince).

## Host & shutdown
Host ara-build için kapatıldı, restart EDİLMEDİ (PC shutdown geliyor). PC kapatılıyor.
**Uyarı:** task_fe0d89d1 (Country-seed fix) ayrı oturumdaydı — PC shutdown onu da keser; sabah tekrar başlatılabilir.
