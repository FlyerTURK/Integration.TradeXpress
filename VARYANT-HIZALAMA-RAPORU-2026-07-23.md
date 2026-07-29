# Varyant Hizalama — Otonom Uygulama Raporu (2026-07-23)

> İki çok-ajanlı denetim (muadil derin-denetimi 38 ajan + geniş emtia-varyant denetimi 22 ajan, tümü
> adversaryel-doğrulamalı) → bulgular birleştirildi → güvenli kalemler otonom uygulandı → gerisi karar listesi.
> **Commit YOK** — tümü diskte, `3190e4f` üstüne. Menü düzenlemesi de bu pakette.

## UYGULANAN (tamamı build+test doğrulamalı)

| # | Düzeltme | Etki |
|---|---|---|
| **A1** | `RecipeCostPopulator` — işçilik artık satırın **seçili varyantından** (canlı-okuma varyant-anahtarlı sözlük; legacy/kanal satırları ana-varyant fallback) | Product+N11+Trendyol+Etsy+Muadil **5 alt sistemin para hesabı**; UI-sunucu 6-vs-30 çelişkisi kapandı |
| **Bulgu-A** | `SubstitutionCalculationAppService` işçilik join'i `Disable<IMultiTenant>+<ICompanyScoped>` içine alındı + EntityName guard + işçilik-eksikte LOG uyarısı | Tenant'ta muadil sıralaması artık **gerçek işçilikle** (sessiz-0/yanlış Rank bitti) |
| **A3** | Maden raporu GoodReport paritesi: stok `(Maden, Varyant)` kırılımı + Varyant kolonları + filtre | Varyant hareketleri artık ayrı satır |
| **A5** | Sipariş kalem snapshot görseli: varyant-özel → ürün-geneli → herhangi fallback | "Mavi SKU'ya Kırmızı thumbnail" bitti |
| **A7** | Good önizleme/fiyat zenginleştirmesi kardeş-desen hook'una (filtre-scope İÇİNE) taşındı; picker bacağı dahil | Host-katalog Good'ları tenant'ta thumbnail/fiyat gösterir |
| **Merkezi** | `EntityVariantGraphService.GetActiveVariantOptionsAsync` + `Disable<IMultiTenant>` | Host emtiaların varyant combo'su tenant'ta artık dolu (tüm aile) |
| **Menü** | "Satış" grubu (Satış Kanalları+Kargo Şablonları) · "Raporlar" kök grubu (8 rapor) · Ülkeler+Medya→Tanımlar · günlük-önce kök sırası | İzin/route korunarak |
| **Test** | +2 yeni: tenant-bağlamlı muadil işçilik (maskeleme-kırıcı) + Good host-katalog önizleme | İkisi de düzeltme-öncesi-kırmızı sınıfı |

**Doğrulama:** Full build **0 hata** (1055 uyarı = önceden-var Mapperly full-rebuild taban çizgisi; bugünkü dosyalardan **0** uyarı — kanıtlandı). Domain **303/303** · Application **173/173** · EFCore **199/200** (tek kırmızı = bilinen Country-seed borcu; önceki 2. kırmızı Company artık geçiyor). Host ayakta, temiz.

## MADEN ÇİFTER-KAYIT (senin bulduğun)
Kök neden: 17 Temmuz şirket-sahipliği göçünün bıraktığı şirketsiz (NULL) kopyalar + "NULL herkese görünür" kuralı.
Teşhis: EKUYUMCU 39 NULL (0 referans) + 4A353061 43 NULL (4 varyantlı hariç referanssız); EBE430DC çift DEĞİL.
**Onayladın; izin katmanı beni engelledi** → script hazır: `scratch/metal-null-dedup.sql` (soft-delete, geri-alınabilir,
kendini-koruyan WHERE). Çalıştırma komutu sohbette (Run butonlu).

## SANA KALAN KARARLAR (ayrıntı: ACIK-ISLER.md)
1. **A2** Bilanço Good değerlemesi (bilanço rakamı değişir — birlikte yapalım)
2. **A4** MetalProcessPanel seçili-varyant işçiliği (§9-bitişik; desen hazır)
3. **A6** Kanal reçete `CommodityVariantId` kolonu (migration; A1'in kanal bacağını tamamlar)
4. Ürün kararları: muadil varyant-boyutu · sessiz-0→fail-fast? · rapor işçilik granülerliği · LaborTypeChange bayrakları

## Genel yargı
Denetim öncesi: 1 kırık + 10 kısmen / 12 alt sistem. Bugün uygulananlarla **kırık çekirdek (para-hesabı) ve
5 bağımlı sistemi + 4 okuma/görünürlük kusuru** kapandı; kalanlar bilinçli olarak senin kararına ayrıldı.
