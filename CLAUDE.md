# TradeXpress / Integration — Çalışma Kuralları (TEK KAYNAK)

> Bu dosya projenin **tek always-loaded kural kaynağıdır**. Yeni kural çıkınca AYRI memory dosyası açma — buraya bölüm/madde ekle. Dosyaya-özel detay kurallar `.claude/rules/` altında (`paths:` ile koşullu yüklenir). Pazarlıksız kuralların bir kısmı ayrıca `settings.json` deny + PreToolUse hook ile **mekanik** bloklu.

## 0) Karar yetkisi
- **Teknik/mimari karar bende.** Kullanıcı ihtiyacı/iş hedefini söyler; ben değerlendirir, kararı verir, gerekçesini açıklarım. "Hangisini istersin" diye SORMA. Kullanıcı isterse itiraz eder.
- `AskUserQuestion` yalnız **iş/ürün** kararı için (kapsam, öncelik, ürün hedefi). Teknik seçim için değil.
- Kullanıcının geçmiş teknoloji tercihleri bağlayıcı değil; en uygun çözümü öner, sapıyorsan nedenini söyle.

## 1) Onay gereken işler (önce öner, BEKLE)
- **Denge:** Yapıcı/eklemeli/**geri-alınabilir** normal iş = SORMA, yap (build, kolon/property/alan/dosya ekleme, normal edit, sıralama/stil ekleme). **SORMA gereken = CİDDİ/YIKICI:** çalışan bir şeyi silmek/kökten yeniden yazmak/değiştirmek, geri-dönüşsüz işlem, büyük yapısal dönüşüm. Ölçüt: *geri alması zor mu / mevcut emeği yok ediyor mu?* Evet → DUR + plan (ne·neden·hangi dosya·geri-alınabilir mi) sun + BEKLE.
- **Commit:** onaysız commit YOK. AMA "commit'leyelim mi?" diye SORMA/hatırlatma yapma — kullanıcı farkında, sürekli sormak rahatsız ediyor. Yalnız "commit" deyince yap.
- **Override:** mevcut çalışan stil/ikon/default/davranışı **başka bir şeyle değiştireceksen ÖNCE SOR.** Yeni ekleme serbest; mevcut olanı ezmek onay ister.
- **Çok-dosyalı tekrar:** aynı düzeni 2+ dosyaya uygulayacaksan önce 1 örnek göster/plan sun, onay al, SONRA yaygınlaştır. Topyekûn sweep YOK.
- **CSS/stil:** yeni `.css`/`.razor.css`/`<style>`/sınıf — inline'ı "temizlik" diye CSS'e taşıma dahil — önce dosya+sınıf+seçici+neden belirt, onay bekle.
- **Teşhis önce:** "şöyle olmalı" demeden kök nedeni DOĞRULA (varsayım ≠ kanıt). Referans varsa önce oradaki çalışan deseni incele.

## 2) ASLA onaysız (yıkıcı kısayol YASAK — bir kısmı hook ile de bloklu)
Tıkanınca kolay yola sapıp mevcut işi silme/kökten değiştirme YOK. Refleks: DUR → kök neden + 1-2 küçük geri-alınabilir seçenek → geri-dönüşsüzleri işaretle → onay bekle.
- **DB:** drop/reset, truncate, `WHERE`'siz UPDATE/DELETE, `ef database drop`, `migrations remove`, şema sıfırlama, seed ezme.
- **Git/iş kaybı:** `git reset --hard`, `git checkout -- <dosya>`, force-push, `--amend` ile geçmiş bozma, branch silme, commit'siz değişiklik atma.
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
- **Yerel ≠ Bilanço birimi:** Yerel = ülke parası (CountryCode→TRY); Bilanço = değerleme birimi (`Company.BaseCurrencyUnitId`, HAS olabilir). **KUR görüntüsü → yerele re-base; POZİSYON/değerleme → bilanço birimine re-base.** Karıştırma.
- **ViewModel emekli:** flat edit formları GetDto-direct (`CrudEditComponentBase<TGetDto>`, Save'de `ObjectMapper.Map` Mapperly); drill/tree → Contracts input-DTO + DrillList. Client-side ViewModel YOK.

## 7) Referans kaynakları (ERP iş kuralı araştırması)
"Eski projeye kendiliğinden referans yapma" kuralı **eski KOD/desen/karar taşımak** içindir. **İSTİSNA — bunlar tasarlanmış canlı araştırma kaynağı, voucher/cari/maden/bilanço işinde serbestçe bakılır:**
- **ERPPROV3** (`E:\Kodlarim\ERPPROV3`) — modern ABP yeniden-yazım; pattern/yapı kaynağı.
- **ERPPRO_Source / ERPPRO_Modernized** (`E:\Kodlarim`) — orijinal decompiled C#; iş kuralı **GROUND TRUTH**. Çelişkide orijinali esas al.
- **ERPGOLD DB** — canlı SQL: `.\SQLEXPRESS` / `ERPGOLDV2` / `sa`. SADECE OKU (research); çıkanları `.claude/research/<konu>/` altına kaydet.

## 8) Governance — mekanik konvansiyon ağları (armed)
3 reflection testi `dotnet test`'te kuralı zorlar; KIRMIZIYSA kural çiğnenmiş, sessiz geçilemez. İstisna = allow-list/attribute + gerekçe (asla "testi gevşetme"):
- **EntityConventionTests** — entity ctor'unda `Guid id`/`tenantId` YOK (ABP atar). Allow-list: VoucherLine/BalanceLedgerEntry.
- **AppServiceConventionTests** — elle statik entity→DTO mapper YASAK (Mapperly/`ObjectMapper.Map`). İstisna: Voucher.MapLine (kompozit DTO).
- **NavigationConventionTests** — aggregate'ler arası id-only (XId + aynı adlı nav YASAK; orphan nav YASAK). İstisna: `[AllowNavigation]` (aggregate-içi, ör. VoucherLine.Voucher).
- Yeni kural çıkınca buraya assert ekle (golden GEÇsin, ihlal KIRMIZI).

## 9) Açık işler (pointer — detay yedekte)
- **Governance Faz A** (reflection: ToString override · `SetActive(bool)` not Activate/Deactivate · property `protected set`) → **Faz B** (Roslyn analyzer: ham `ArgumentException`→BusinessException, `Check.NotNullOrWhiteSpace`→StringFieldGuard, expression-bodied).
- **Bullion/Takoz portu:** Assay entity → PT/PD işçilik → motor/poster.
- **Voucher import:** VoucherLineLog + diğer ProcessType'lar.
- **SplitView eski yığını SİLİNMEZ** (`CrudEditComponentBase`/`CrudEditShell`/`{Entity}EditPage` + `SplitCrudView`) — ileride canlandırılacak, dokunma.

---
*Detaylı dosya-özel kurallar: `.claude/rules/`. Eski memory arşivi (tam geçmiş): `.claude/_memory_backup_2026-06-28/`.*
