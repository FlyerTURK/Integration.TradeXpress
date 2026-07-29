# K-Aktivasyon Kapanış Raporu (2026-07-24)

> Kullanıcı talimatı: "K2 ve geriye kalan maddeleri devreye al" + karar yetkisi devri. Tümü faz sırasına ve risk
> disiplinine göre yürütüldü. **Commit YOK** — her şey diskte, `3190e4f` üstüne (varyant-hizalama paketiyle birlikte).

## Uygulanan K-maddeleri (hepsi build 0 hata + testler doğrulamalı)

| K | Ne yapıldı | Migration |
|---|---|---|
| **K1** | Zaten tamdı (çift-yönlü köprü + drill UI) — planda "tam kapandı" işlendi | — |
| **K3** | TrendyolBrand **hybrid cache**: canlı ölçüm (~780K marka, by-name path canlıda doğrulandı, `luxury` alanı keşfi) → kullanıcı **B** onayı → `AppTrendyolBrands` (host-global, ExternalId=long unique) + write-through (Create/Update/Import) + picker açılışta cache'ten (form açılışında canlı API çağrısı kalmadı) | `AddTrendyolBrandCache` ✓ uygulandı |
| **K4** | MaxPurchaseQuantity: Product varsayılanı + N11 override **zaten varmış** (keşif) → çekirdek ShipmentTemplate'ten çıkarıldı (DTO/UI/Apply; entity property Faz-4'e obsolete-yorumlu), devralma `ChannelInheritance.Resolve`'a bağlandı, tasarım dokümanı senkronlandı | gerekmedi |
| **K8-Faz1** | `ShipmentTemplateName` okuma-yolu FK'ye (çekirdek rename → anında güncel ad); N11 push adı **kanal-ilk** zincirle (canlı ayna → FK-onarım+log → ham fallback); kolon kaldırma Faz-4 | — |
| **K9** | `EtsyWhenMade`(8) → `ProductMadePeriod`(19, kronolojik; git-mv geçmişli) + kolon rename; Etsy wire-map 19 değer + bilinmeyen→fail-fast (gizli kayıp kapandı); 31 pinli test + 40 lokalizasyon anahtarı; öncesi/sonrası sqlcmd assert (veri %100 değer-0, remap no-op kanıtlı) | `RenameWhenMadeToMadePeriodAndExpand` ✓ uygulandı |
| **K10+K11** | `ChannelInheritance` merkezî devralma helper'ı (kanal-dolu-ise-kanal-değilse-ürün): kişiselleştirme blok-çözümü + AddOn efektif-değer (katalog-eksik → fail-fast); Etsy push'a hazır-bekler; 8 pinli test | — |
| **K12** | Import stok politikası: ilk-import tohumlar (pinli), sonraki importlarda çekirdek EZİLMEZ → `OverrideStock` (üç kanalda zaten vardı) + satır-log + `StockDifferenceCount` sayacı panelde; **Etsy'nin remote stoğu görünmez düşürdüğü** eski davranış da kapandı | gerekmedi |
| **K14** | Hesap-planı eşleme KARARI dokümante: kanal-başına tek cari `MP-{Kanal}`, Accepted-tetikli idempotent satış fişi, komisyon Service / payout Cash / iade Return ters-fiş (`.claude/research/marketplace/K14-order-voucher-hesap-plani.md`) — Faz-3 `OrderPostingManager` dilimi buna dayanacak | — |
| **K2** | Dondurma kuralı CLAUde.md'de: yeni görsel özelliği YALNIZ DAM'a; ProductImage/MetalImage donmuş legacy; emeklilik Faz-5'te ayrı planla | — |
| K5/K6/K7/K13 | Öneri gereği kendi fazında (ertelendi/Faz-3+) | — |

## Final doğrulama
- Build: **0 hata** · Domain **303/303** · Application **213/213** (bu gece +40 yeni test) · EFCore **198/200** (2 kırmızı = bilinen eski Country/Company seed borcu, K-işleriyle ilgisiz)
- İki migration DB'ye uygulandı ve sqlcmd ile doğrulandı; host ayakta, temiz log.
- Research güncellemeleri: Trendyol by-name ✓ + ~780K ölçümü; K9 dry-run raporu; K14 karar dokümanı.

## Bekleyenler (sırası gelince)
- Faz-3: `OrderPostingManager` (K13a/K14 — hesap-planı hazır) · Faz-4: K8 kolon kaldırma + N1 id-only + T1 · Faz-5: K2 DAM emekliliği + K13b projeksiyon
- Varyant-hizalama raporundaki kullanıcı-kararları: A2 bilanço değerleme · A4 §9 panel · A6 kanal-reçete kolonu
- Eski borç: Country/Company seed regresyonu (2 test kırmızı)
