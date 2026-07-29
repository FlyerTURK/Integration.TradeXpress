# Gece Otonom İş Raporu — 2026-07-22

> Umut uyurken uçtan uca tamamlanması istenen iş. Aşağıdaki her şey **commit EDİLMEDİ** (kuralın gereği:
> yalnız "commit" deyince commit yapılır). Değişiklikler diskte kayıtlı. Sabah gözden geçirip commit'lersin.

## HEDEF (istenen)
Çift-yönlü **çekirdek ↔ N11 kargo şablonu köprüsü**nün (Item 1) uçtan uca bitirilmesi + N11 kanal
varsayılan metinleri. Bitince bilgisayarın kapatılması.

## TAMAMLANAN — hepsi doğrulandı (build 0/0, ilgili testler yeşil)

### 1a) Reverse köprü (kanal → çekirdek otomatik)
- `IShipmentTemplateReconciler` + `ShipmentTemplateReconciler` (Application\Shipments) — YENİ.
- N11 kargo şablonu kaydedilince (Create/Update/Import) `ShipmentTemplateId==null` ise, aynı şirkette
  `Code==NormalizeCode(TemplateName)` çekirdeği bul-veya-oluştur + `SetCoreTemplate` ile bağla. Idempotent,
  origin-guard'lı. WarehouseAddress → çekirdek DispatchAddress (CloneAddress ile taze VO).

### 1b) N11 kanal varsayılan bilgi metinleri
- `SalesChannelTrN11`: `DefaultShippingInfo` / `DefaultExchangeInfo` / `DefaultInstallmentInfo` (opsiyonel) +
  `SetDefaultInfos`.
- **Migration** `20260722091854_AddN11ChannelDefaultInfos` — yalnız `AppSalesChannelTrN11`'e 3 nullable kolon.
  **TradeXpress DB'ye uygulandı ve sqlcmd ile doğrulandı** (3 kolon, nullable, nvarchar(1024)).
- N11 kanal formunda "N11 Kargo Varsayılanları" grubu; yeni N11 kargo şablonu formu bu varsayılanlarla
  ön-doldurulur (`N11ShipmentTemplateDrill.NewTemplate()`).

### 1c) Forward köprü (çekirdek → kanal, elle giriş bypass)
- Backend: `IShipmentTemplateAppService.GetChannelDeploymentsAsync` (kanal dağıtımlarını listeler, agnostik) +
  `IN11ShipmentTemplateAppService.BuildDeploymentDraftAsync` (PERSIST ETMEZ; çekirdek + kanal varsayılanından
  ön-doldurulmuş `N11ShipmentTemplateCreateDto` üretir; `ShipmentTemplateId` forward link).
- UI: `ShipmentTemplateChannelDrill` (çekirdek formda "Satış Kanalları" sekmesi) — Ekle (kanal seç → taslak →
  N11 edit formunda tamamla → `CreateAsync` = **validation + N11 push**), Düzenle (`UpdateAsync`), Sil (N11
  silme API'si yok → bilgi popup'ı). Mevcut `N11ShipmentTemplateEditFields` aynen reuse edildi.
- **Kilit davranış (onaylı):** eksik zorunlu alanlı N11 şablonu SESSİZCE kaydolmaz — `EnsureN11Requirements`
  validation'ından geçer. "Bypass" = ön-doldurma; validation bypass DEĞİL.
- Forward link (`ShipmentTemplateId`) round-trip'te korunuyor (CreateDto↔GetDto Mapperly) → reverse-reconcile
  origin-guard'la atlar, çift çekirdek üretilmez. (Doğrulandı.)
- InstallmentInfo etiketi "Kurulum" → **"Taksit / Vade Farkı Bilgileri"** düzeltildi (N11 SOAP kaynağıyla).

## DOĞRULAMA
- **Tüm solution build:** `Integration.TradeXpress.slnx` — **0 uyarı, 0 hata.**
- **Domain testleri:** 303/303 ✅ (EntityConvention/Navigation/Razor/**LocalizationParity** dahil).
- **Application testleri:** 173/173 ✅ (AppServiceConvention — manuel mapper yasağı dahil).
- **EF Core testleri:** 196/198 — 2 hata (aşağıda; item-1 DIŞI, önceden bozuk).

## ⚠ DİKKAT — 2 EF testi KIRMIZI (bugünkü işle İLGİSİZ, önceden bozuk)
`EfCoreCountryAppServiceTests.Host_should_have_seeded_country_catalog` ve
`EfCoreCompanyAppServiceTests.Host_cannot_create_a_company` — ikisi de `.Single(...)` "Sequence contains no
matching element".

**Teşhis:**
- Country testi: `TotalCount>=10` GEÇİYOR ama `Single(c=>c.Code=="TR")` "no match" → ≥10 ülke dönüyor, içinde
  **"TR" kodlu ülke YOK**. `CountrySeeder.cs:105` hâlâ `("TR","Türkiye")` (commit'li, değişmemiş); test de
  değişmemiş. Kıran şey bu oturumda **daha önce** yapılan **uncommitted ülke-adı-İngilizce + coğrafya reworkü**
  (`Country.cs`, `GeographySeeder.cs`, `CompanyAppService.cs` git'te "M"). Muhtemelen seed sırasında bir
  regresyon (kod/isim eşleme ya da seed contributor'da yutulan exception) TR'nin oluşmasını/dönmesini bozuyor.
- **Bu 2 hata bugünkü kargo-köprüsü (item-1) çalışmasının DOKUNMADIĞI alanda.** Kanıt: item-1 dosyaları
  (Shipment/N11/SalesChannel) ile Country/Company/Geography seeding arasında sıfır kesişim; hatalı test
  dosyaları da değişmemiş.

**Neden gece düzeltmedim:** core seeding + önceki "ülke adları İngilizce" kararının bağlamını gerektiriyor;
körlemesine dokunmak (§5 plan-önce; §2 çalışan şeyi ezme riski) durumu kötüleştirebilir. Senin kararını ister.
→ ACIK-ISLER.md'ye işlendi.

## Sıradaki (uyanınca)
- Yukarıdaki 2 Country/Company seed regresyonunu teşhis/düzelt (ülke-İngilizce reworkünün yan etkisi).
- Item 2 (Takoz `/1000` işçilik ölçeği + BullionBalancePoster null-kur sessiz 0) — planlandı, başlanmadı.
- Forward/defaults canlı UI testi (host'ta): kargo şablonu → "Satış Kanalları" → Ekle akışı.

## Commit durumu
Hiçbir şey commit edilmedi. `git status` tüm değişiklikleri gösterir. Sen "commit" deyince commit ederim.
