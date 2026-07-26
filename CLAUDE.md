# TradeXpress / Integration — Çalışma Kuralları (TEK KAYNAK)

> **Kullanıcı = HAKAN** (hitap: Hakan). Makine/git hesabı ("Umut"/umutt) ve ts.net adı OĞLUNUN adınadır — kullanıcı adı sanıp "Umut" diye hitap ETME (2026-07-24 düzeltmesi).

> Bu dosya projenin **tek always-loaded kural kaynağıdır**. Yeni kural çıkınca AYRI memory dosyası açma — buraya bölüm/madde ekle. Dosyaya-özel detay kurallar `.claude/rules/` altında (`paths:` ile koşullu yüklenir). Pazarlıksız kuralların bir kısmı ayrıca `settings.json` deny + PreToolUse hook ile **mekanik** bloklu.

## 0) Karar yetkisi
- **Teknik/mimari karar bende.** Kullanıcı ihtiyacı/iş hedefini söyler; ben değerlendirir, kararı verir, gerekçesini açıklarım. "Hangisini istersin" diye SORMA. Kullanıcı isterse itiraz eder.
- `AskUserQuestion` yalnız **iş/ürün** kararı için (kapsam, öncelik, ürün hedefi). Teknik seçim için değil.
- Kullanıcının geçmiş teknoloji tercihleri bağlayıcı değil; en uygun çözümü öner, sapıyorsan nedenini söyle.

### 0.1) Ön-uçuş kontrol listesi (2026-07-25)
> **"Meclis" (5 lens + kör subagent oturumu) AYNI GÜN KALDIRILDI.** Hakan'ın gerekçesi: *"aşırı zaman alıyordu. seninle zaten ultracode ile çalışıyoruz ve senin alt agent çalıştırma düzenini bozma ihtimalinden çekindim."* Yani asıl sorun isabetsizlik değil **ÇİFT ORKESTRASYON**: ultracode zaten göreve göre subagent dallandırıyor; meclis onun üstüne SABİT 5 lensli rakip bir katman koyuyordu (3 oturum · 22 dk · ~1,1M token). Lens töreni · kademe beyanı · kör meclis · K0/K1/K2 kademeleri: **hepsi kalktı.** Fan-out artık göreve göre şekillenir, sabit şablona göre değil. Tarihçe: `.claude/research/governance/MECLIS-LOG.md`.

**Kalan TEK şey — ÖN-UÇUŞ. Her kod işinde, maliyet ~0, ATLANAMAZ.** Kod yazmadan önce 4 kutu:
`[varsaydım mı / doğruladım mı?]` · `[bu bileşen-servis zaten var mı — grep'ledim mi?]` · `[hangi test kırılabilir?]` · `[geri alınabilir mi?]`

**SESSİZ uygulanır** — beyan yok, tören yok, ek çıktı yok. Yalnız bir kutu KIRMIZI çıkarsa tek satırla söylenir.

*Neden bu kaldı: 2026-07-25'in belgeli hatalarını yakalayan şey meclis DEĞİL buydu — N11 "1"≠"01" · elde yeniden yazılan `BranchEditFields` · kırılan `OrgCodeUniquenessTests` · zaten var olan `ShowValidationToasts`. Kutu #2'yi UI'da sorup DOMAIN servislerinde sormamak da `GeographyResolver`'ı üçüncü kez yazmama yol açtı → kutu #2 **her katman için** sorulur.*

Geri-dönüşsüz · mimari · kural değiştiren işlerde ayrı mekanizma YOK: **§1 aynen geçerli** (DUR + plan + BEKLE + onay).

## 1) Onay gereken işler (önce öner, BEKLE)
- **Denge:** Yapıcı/eklemeli/**geri-alınabilir** normal iş = SORMA, yap (build, kolon/property/alan/dosya ekleme, normal edit, sıralama/stil ekleme). **SORMA gereken = CİDDİ/YIKICI:** çalışan bir şeyi silmek/kökten yeniden yazmak/değiştirmek, geri-dönüşsüz işlem, büyük yapısal dönüşüm. Ölçüt: *geri alması zor mu / mevcut emeği yok ediyor mu?* Evet → DUR + plan (ne·neden·hangi dosya·geri-alınabilir mi) sun + BEKLE.
- **Commit:** onaysız commit YOK. AMA "commit'leyelim mi?" diye SORMA/hatırlatma yapma — kullanıcı farkında, sürekli sormak rahatsız ediyor. Yalnız "commit" deyince yap.
- **Override:** mevcut çalışan stil/ikon/default/davranışı **başka bir şeyle değiştireceksen ÖNCE SOR.** Yeni ekleme serbest; mevcut olanı ezmek onay ister.
- **Çok-dosyalı tekrar:** aynı düzeni 2+ dosyaya uygulayacaksan önce 1 örnek göster/plan sun, onay al, SONRA yaygınlaştır. Topyekûn sweep YOK.
- **CSS/stil:** yeni `.css`/`.razor.css`/`<style>`/sınıf — inline'ı "temizlik" diye CSS'e taşıma dahil — önce dosya+sınıf+seçici+neden belirt, onay bekle.
- **Teşhis önce:** "şöyle olmalı" demeden kök nedeni DOĞRULA (varsayım ≠ kanıt). Referans varsa önce oradaki çalışan deseni incele.

## 2) ASLA onaysız (yıkıcı kısayol YASAK)
Tıkanınca kolay yola sapıp mevcut işi silme/kökten değiştirme YOK. Refleks: DUR → kök neden + 1-2 küçük geri-alınabilir seçenek → geri-dönüşsüzleri işaretle → onay bekle.
- **Mekanik bloklu** (deny + `guard.ps1` hook — tam liste orada): git reset --hard / checkout -- / force-push / --amend / branch -D / clean -f · `ef database drop` / `migrations remove` · rm -rf & toplu silme · anonim-erişim attribute edit'i · Migrations/snapshot elle edit. Aynı sınıf (hook'suz): `WHERE`'siz UPDATE/DELETE, truncate, şema sıfırlama, seed ezme, commit'siz değişiklik atma.
- **Kod:** kökten yeniden yazım, çalışan implementasyonu silme, tıkandın diye yaklaşımı terk etme.
- **Mimari:** katman kaydırma (UI↔API), merkezi yolu (CrudLayout/StateService/Framework modülü) bypass edip paralel tek-kullanımlık yapı.
- **Güvenlik:** auth/`[Authorize]` kapatma, multi-tenant `IDataFilter` disable, validation/concurrency/guard kaldırma "çalışsın diye".
- **Sahte baypas:** mock/fake dönme, `catch {}` ile kök neden gizleme, `#pragma warning disable`/`<NoWarn>`, test/assertion zayıflatma.
- **Diğer:** sabit paket sürümü (DevExpress 25.2.5/ABP) hatadan kaçmak için oynama; sessiz kapsam düşürme; hedefli düzeltme yeterken toplu find-replace/reformat; Program.cs/DI/DbContext/modül global config'i yerel semptom için değiştirme; kullanıcının çalışan host'unu habersiz öldürme.

## 3) Build / host akışı
- Host'u **terminal sahiplenir** (VS değil, F5 yok). Host'u ben `dotnet run -c Debug` ile başlatırım.
- Değişiklikte **sadece `dotnet build` + doğrula + kısa rapor**. **Otomatik kill+restart ETME** — kullanıcının odağı dağılıyor. Restart'ı yalnız "yenile/restart/ayağa kaldır" deyince ya da bir grup değişiklik bitince yap.
- **Aradaki adımlarda derleme bile yapma** — değişiklikleri biriktir, grup sonunda derle.
- **Host çalışırken build = stale DLL** ([dll-kilit]): önce host'u ÖLDÜR (`Get-NetTCPConnection -LocalPort 44318 -State Listen | Stop-Process`), port boş doğrula, SONRA build, sonra `dotnet run --no-build`.
- Server-side değişiklik Blazor host'u (44318) restart ister, HttpApi.Host (44388) değil (in-process).

## 4) Kod stili & isimlendirme
- **Identifier'lar İNGİLİZCE** (class/property/method/enum/const/namespace/dosya). **Yorumlar Türkçe.** ERP terimi İngilizce'ye çevrilir: Bilanço→BalanceSheet, Cinsi→Category, Bakiye→Amount/Balance, Takoz→Bullion, Pırlanta→Jewelry (bizde Mücevher), Taş→Stone, İşçilik→Labor, Çeşni→Alloy, Şube→Branch, Kasa→Vault, Hesap→Account. Emin değilsen sor.
- **Pazaryeri "Listing" kavramı YASAK** (2026-07-07 kullanıcı kararı): kanal-ürün entity'si `SalesChannelTr{Pazaryeri}Product` deseninde adlandırılır (ör. `SalesChannelTrN11Product`, `SalesChannelTrTrendyolProduct`) — kanal ailesi `SalesChannelTr{Pazaryeri}` ile hizalı. Push metotları `PushTo{Pazaryeri}Async`.
- **Hiçbir tipi inline tam-nitelikli yazma** (her tip, sadece Guid değil): `System.Guid`→`Guid`. Namespace ön-eki = koku. C# namespace'leri **GlobalUsings.cs**'te, Razor `@using`'leri **_Imports.razor**'da topla; kodda kısa ad. Redundant/duplike using'i o dosyaya dokununca sil — ama INCREMENTAL, ayrı "using refactor" görevi açma.
- **Mühendislik prensiplerini aktif uygula** (DRY/SOLID + KISS/YAGNI/Composition-over-Inheritance/Connascence/Fail-fast/SSOT/Tell-Don't-Ask/Encapsulation/Least-Astonishment...). Dokunduğun kapsamda ihlal görürsen **sadece raporlama — düzelt.** En üst bilinç: **kod ne kadar merkezi/yeniden-kullanılabilir olursa diğer UI/yeni projelerde devralınması o kadar kolay** → en merkezi, override ile genişleyen yerleşimi seç. Generic/reusable kod **Framework**'e (TradeXpress'e değil).

## 5) Dosya / klasör düzeni (çöplük yapma)
- **Feature-klasör = namespace.** Namespace klasör yolunu birebir izler, TÜM katmanlarda + testlerde aynı (ör. `Financials/{CurrencyUnits,Parities,ExchangeRates}`). Yeni dosya doğru bucket'a, kök/gevşek dizine bırakma.
- **Taşıma `git mv`** (geçmiş korunur). **EF Migrations/snapshot'a DOKUNMA** — namespace değişse de snapshot bayatlar ama runtime'ı etkilemez, sonraki gerçek migration'da yenilenir.
- **Plan-modu disiplini:** DB migration · çekirdek iş mantığı · prod/güvenlik config → uygulamadan ÖNCE plan sun.
- **Git güvenlik ağı:** her oturum öncesi temiz commit; bozulursa geri al.

## 6) Mimari sabitler (stack)
- **ABP.IO** (ücretsiz) + **DevExpress.Blazor 25.2.5** (lokal NuGet, TAM sabitleme — sürüm oynama) + **EF Core 10**. `Integration.Framework` = ayrı Core değil, her projeye `DependsOn` ile eklenen **ABP modülü**.
- **Blazor SERVER** (WASM değil): `ILogger` server-log'a gider; `IWebAssemblyHostEnvironment` YOK; client modülü server'ın `DependsOn`'unda değil → **servis/mapper'ı server modülde de elle kaydet**; `BusinessException` in-process lokalize olmaz.
- **Portlar:** Blazor host `:44318` · HttpApi.Host `:44388`. Kanonik URL `https://umut.taile7a850.ts.net:44318` (WASM authority tek-değerli → localhost değil ts.net). Cert: `E:\Kodlarim\Yeni\certs\`.
- **Org hiyerarşisi:** Şirket→Şube→Kasa (OrgTreeManager: otomatik kurulum + en-az-1-child + HQ-devir-önce-sil). Yetki: rol/izin tenant/company/branch/vault **scoped** (cascade + dar override; working context = company+branch).
- **TÜM emtia katalogları PER-COMPANY** (2026-07-25, görev #4): Metal·Stone·Jewelry·Good·Scrap·Future·Service **`ICompanyOwned`** — `CompanyId` ZORUNLU (`Guid`, `Guid?` değil), benzersizlik `(TenantId, CompanyId, Code)` + soft-delete farkındalı filtre. Sahiplik client'tan DEĞİL working company'den damgalanır (`CompanyOwnershipGuard.ResolveOwnerCompanyId` → şirket yoksa fail-closed). Seeder'lar tenant'ın HER şirketine ayrı set kurar (org seed'inden SONRA). **Eski "emtialar host-seviyesi (TenantId=null)" inancı YANLIŞTI** (canlıda 0 host satırı) ve sahipsiz "holding" katmanı cross-company manipülasyonun taşıyıcısıydı — kaldırıldı. Mekanik ağ: `CompanyScopedFilterTests.Ownerless_commodity_cannot_be_constructed` (7 aile). *(Cash + CurrencyUnit + Country hâlâ host‖tenant kataloğu — emtia değil.)*
- **KARGO YALNIZ SATIŞ KANALI SEVİYESİNDE** (2026-07-26 Hakan kararı — çekirdek katman SİLİNDİ): çekirdek `ShipmentTemplate` + `TrCarrier` entity/servis/UI/tablolarının TAMAMI kaldırıldı; ürün üzerindeki kargo şablonu bağı (`Product.ShipmentTemplateId`/`ShipmentTemplateName`) ve N11 K1 köprüsü (`N11ShipmentTemplate.ShipmentTemplateId`) de sökülüldü. **Gerekçe:** kargo şablonu ürünün değil KANALIN özelliğidir (aynı ürün her pazaryerinde farklı şablonla gider) ve her pazaryerinin kendi kuralları olduğundan ortak çekirdek katman gereksiz soyutlamaydı. Kalan yapı: `N11ShipmentTemplate` (kanal şablonu) + `N11ShipmentCompany` (host-global N11 firma aynası). **Sistem CARİ AÇMAZ:** kısa ömürlü `KARGO` hesabı + 68 alt hesap otomasyonu da geri alındı — şirket kendi cari planını kendisi yönetir; kargo firmasının carisi kullanıcı tarafından bağlanır. **Hedeflenen model** (henüz kurulmadı): kargo firmaları kanal başına tanımlanır, varsayılan cari alt hesap ilk kullanımda (şablon içe aktarımında, firma öksüzse) kullanıcıya SORULUR; aynı firma farklı kanallarda aynı cariyi gösterebildiğinden borç tek bakiyede birikir. Reçetedeki kargo kalemi ORTALAMA maliyettir (tek paket varsayımı — fiyatlama için); gerçek maliyet sipariş sürecinde kullanıcı tarafından girilir.
- **Yerel ≠ Bilanço birimi** — kur görüntüsü YERELE, pozisyon/değerleme BİLANÇOYA re-base; karıştırma. Detay + yön kuralları: `.claude/rules/financials.md`.
- **Zaman: kayıt=UTC, görüntü=kullanıcı yerel saati** (2026-07-03 ürün kararı). Zaman damgaları UTC saklanır (`AbpClockOptions.Kind=Utc` hedefi); UI her kullanıcının tarayıcı/masaüstü saatine çevirip gösterir (merkezi dönüşüm — sayfa-başı elle çeviri YOK). İSTİSNA: kullanıcının seçtiği İŞ TARİHİ alanları (VoucherDate gibi) date-only semantiktir, timezone kaydırmasına GİRMEZ (gün kayması yasak). Geçiş planı: zaman/kültür denetim raporu.
- **ViewModel emekli:** flat edit formları GetDto-direct (`CrudEditComponentBase<TGetDto>`, Save'de `ObjectMapper.Map` Mapperly); drill/tree → Contracts input-DTO + DrillList. Client-side ViewModel YOK.
- **TEK fiziksel DB** (2026-07-10 keşfi): `AbpTenantConnectionStrings` boş — 14 tenant'ın tümü tek `TradeXpress` DB'sini paylaşıyor; migration'lar TEK DB'ye uygulanır ("3 DB'ye uygula" adımı YOK). `Integration.TradeXpress_company1/_ekuyumcu/_Service` DB'leri ESKİ projelerin kalıntısı, bu app'in değil (DOKUNMA). DbMigrator secrets için proje klasöründen çalıştırılır (repo kökünden content-root bulunamaz).
- **Görsel sistemi DONDURMA (K2, 2026-07-23 aktivasyon):** yeni görsel/medya özelliği YALNIZ DAM'a (Media/EntityMediaLink) yazılır; `ProductImage`/`MetalImage` DONMUŞ legacy — yeni özellik ALMAZ (bugfix serbest). Migrate+emeklilik Faz-5'te ayrı planla (master plan K2).

## 7) Referans kaynakları (ERP iş kuralı araştırması)
"Eski projeye kendiliğinden referans yapma" kuralı **eski KOD/desen/karar taşımak** içindir. **İSTİSNA — bunlar tasarlanmış canlı araştırma kaynağı, voucher/cari/maden/bilanço işinde serbestçe bakılır:**
- **ERPPROV3** (`E:\Kodlarim\ERPPROV3`) — modern ABP yeniden-yazım; pattern/yapı kaynağı.
- **ERPPRO_Source / ERPPRO_Modernized** (`E:\Kodlarim`) — orijinal decompiled C#; iş kuralı **GROUND TRUTH**. Çelişkide orijinali esas al.
- **ERPGOLD DB** — canlı SQL: `.\SQLEXPRESS` / `ERPGOLDV2` / `sa`. SADECE OKU (research); çıkanları `.claude/research/<konu>/` altına kaydet.

## 8) Governance — mekanik konvansiyon ağları (armed)
Kurallar MEKANİK zorlanır (derleme + test); KIRMIZIYSA kural çiğnenmiş, sessiz geçilemez. İstisna = allow-list/attribute + gerekçe (asla "testi gevşetme" / `#pragma`). Allow-list'ler ve tam gerekçeler İLGİLİ dosyaların içinde yaşar — burada tekrarlanmaz.
- **Derleme-zamanı** (yalnız Domain+Domain.Shared; BannedApiAnalyzers RS0030=error): `Guid.NewGuid` · ham .NET exception ctor'ları (→ BusinessException/tipli) · `Check.NotNullOrWhiteSpace` (→ StringFieldGuard) — tam liste kök `BannedSymbols.txt`. Expression-bodied member: kökte warning, `Domain*/.editorconfig` ERROR (auto-prop + lambda muaf).
- **Test-zamanı:** EntityConventionTests (ctor'da id/tenantId yok · SetActive(bool) · ToString · protected set) · AppServiceConventionTests (elle statik entity→DTO mapper YASAK) · NavigationConventionTests (aggregate'ler arası id-only) · RazorConventionTests (yeni .razor'da `@code` YASAK · ad-hoc sembol/emoji ikon YASAK · yeni tam-nitelikli `@inject` YASAK) · LocalizationParityTests (tr/en anahtar paritesi).
- Yeni kural çıkınca buraya assert/ban ekle (golden GEÇsin, ihlal KIRMIZI).

## 9) Açık işler & dokunulmazlar
- Açık işler listesi: `.claude/research/governance/ACIK-ISLER.md` (Governance Faz A/B · Bullion portu · Voucher import · EditHost boilerplate).
- **SplitView eski yığını SİLİNMEZ** (`CrudEditComponentBase`/`CrudEditShell`/`{Entity}EditPage` + `SplitCrudView`) — ileride canlandırılacak, dokunma.

## 10) Subagent orkestrasyonu
- **Kararsızlıkta SOR (agent→main):** Subagent kritik bir kararda (mimari · iş kuralı · geri-dönüşsüz işlem) emin olamazsa KOD YAZMADAN DUR, `SendMessage` ile main'e (bana) sorsun, cevabı bekleyip ona göre hareket etsin. Tahminle ilerleyip yanlış yol açmasın. **Her görev prompt'una bu talimatı ekle.**
- **Rapor→düzeltme döngüsü (main→aynı agent):** Subagent işi bitirip çözüm/rapor sunduğunda, endişelerimi + tespit ettiğim yanlış/eksikleri AYNI agente (context sıcak) `SendMessage`/resume ile ilet; düzeltip tamamlasın. Sıfırdan yeni agent açma — bağlamı koru.

---
*Detaylı dosya-özel kurallar: `.claude/rules/`. Eski memory arşivi (tam geçmiş): `.claude/_memory_backup_2026-06-28/`.*
