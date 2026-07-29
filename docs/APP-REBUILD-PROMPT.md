# TradeXpress — Farklı Bir Platformda Yeniden İnşa Prompt'u

> **Bu dosya nedir?** Başka bir yapay zekâ ajanına verilecek, TradeXpress uygulamasının
> **ne yaptığını** ve **hangi iş kurallarıyla** çalıştığını eksiksiz anlatan spesifikasyondur.
> Amaç: ajanın bu uygulamayı **istediği teknoloji yığınında sıfırdan yazabilmesi**.
> Aşağıdaki metnin tamamı ajana verilecek prompt'tur; "mevcut sistem" diye geçen her şey
> davranışsal referanstır, kod kopyalanacak diye değil.

---

# BÖLÜM 0 — AJANA GÖREV TALİMATI

Sen kıdemli bir yazılım mimarı ve tam-yığın geliştiricisin. Aşağıda **TradeXpress** adlı
çalışan bir kurumsal uygulamanın tam davranış spesifikasyonu var. Görevin bu uygulamayı,
sana söylenen (ya da senin gerekçelendirerek seçeceğin) **farklı bir teknoloji platformunda
sıfırdan yeniden inşa etmek**.

**Uyman gereken kurallar:**

1. **Davranışı koru, teknolojiyi değiştir.** Spesifikasyondaki iş kuralları, değişmezler
   (invariant), hesap formülleri, yön/işaret konvansiyonları ve güvenlik sınırları
   **pazarlıksızdır**. Kullandığın framework, ORM, UI kütüphanesi serbesttir.
2. **Hiçbir iş kuralını "basitleştirme".** Bu bir muhasebe + stok + pazaryeri entegrasyon
   sistemidir; yuvarlama, işaret, tarih semantiği gibi ayrıntılar paranın kendisidir.
3. **Kurguyu katmanlı yap.** Domain (iş kuralı) → Uygulama (orkestrasyon/yetki) →
   Altyapı (kalıcılık/dış API) → Sunum (UI). Domain katmanı hiçbir altyapıya bağımlı olmasın.
4. **Kararsız kaldığın her yerde DUR ve sor.** Mimari, iş kuralı ya da geri-dönüşü olmayan
   bir işlem konusunda emin değilsen kod yazma; sorunu net biçimde sahibine ilet, cevabı
   bekle. Tahminle ilerleyip yanlış yol açma.
5. **Fazlarla ilerle.** Aşağıdaki Bölüm 14'teki inşa sırasını izle; her fazın sonunda
   çalışan + test edilebilir bir dilim teslim et.
6. **Test ağlarını kur.** Bölüm 13'teki konvansiyon testleri sistemin kendini koruma
   mekanizmasıdır; taşınmazsa mimari birkaç hafta içinde çürür.

---

# BÖLÜM 1 — UYGULAMA NE YAPAR (ÖZ)

**TradeXpress**, kuyumculuk / kıymetli maden ticareti yapan işletmeler için yazılmış,
çok-kiracılı (multi-tenant) bir **ERP + pazaryeri entegrasyon platformudur**.

İki büyük yeteneği tek veri modelinde birleştirir:

### A) Kıymetli maden ticaret defteri (ERP çekirdeği)
- Cari hesap, kasa, şube, şirket hiyerarşisi üzerinde **fiş (voucher) tabanlı işlem kaydı**.
- Altın/gümüş/platin/paladyum gibi madenlerin **milyem (ayar/saflık) bazlı** alım-satımı;
  nakit döviz, hurda, taş, mücevher, mamül, hizmet, vadeli işlem, takoz (külçe), çeşni,
  dekont ve virman işlemleri.
- Her işlemin **birim bazında bakiye etkisi** kalıcı bir deftere (ledger) yazılır; pozisyon
  ve bilanço raporları bu defteri okur.
- Canlı döviz/maden kuru beslemesi, parite yönetimi, marj/türetme kuralları.

### B) Pazaryeri satış entegrasyonu
- **N11**, **Trendyol** ve **Etsy** pazaryerlerine ürün listeleme, fiyat/stok güncelleme,
  sipariş çekme.
- Ürünün maliyeti bir **reçete (BOM)** ile kurulur: maden + taş + işçilik + hizmet +
  komisyon + kargo + paketleme. Satış fiyatı bu maliyetten canlı kur ile türetilir.
- **Muadil (ikame) motoru**: "12 gram ziynet" gibi bir hedef gramajı, eldeki maden
  varyantlarının kombinasyonlarıyla karşılayıp otomatik ürün varyantı üretir; maden stoğu
  değişince kombinasyonlar ve satılabilir adet yeniden hesaplanır (aşırı satış koruması).

**Tek cümleyle:** *Kuyumcunun defterini tutan, madenin milyemini ve kurunu doğru işleyen,
aynı zamanda o madenden üretilen ürünü üç pazaryerinde satan bütünleşik sistem.*

### Mevcut yığın (referans — birebir taşınması ZORUNLU DEĞİL)
| Katman | Bugün kullanılan |
|---|---|
| Platform | .NET 10, C# |
| Uygulama çatısı | ABP.IO 10.4.1 (modüler monolit, DDD) |
| ORM / DB | EF Core 10 · SQL Server (TEK fiziksel DB, tüm kiracılar paylaşır) |
| UI | Blazor **Server** + DevExpress.Blazor 25.2.5 |
| Kimlik | OpenIddict (OAuth2/OIDC) + ABP Identity |
| Loglama | Serilog |
| Test | xUnit + Shouldly + NSubstitute + bUnit |
| Ölçek | ~190 domain sınıfı, ~70 uygulama servisi, ~190 UI bileşeni, ~190 test dosyası, 31 migration, 2 tam dil (TR/EN, ~2.000 anahtar) |

---

# BÖLÜM 2 — KİRACILIK, ORGANİZASYON VE YETKİ MODELİ

Bu bölüm sistemin **güvenlik iskeletidir**. Yanlış kurulursa bir şirketin kullanıcısı
kardeş şirketin verisini görür/değiştirir. Aşağıdaki katmanlar **kümülatiftir**.

## 2.1 Kiracı (Tenant)
- Klasik multi-tenancy: her kayıt `TenantId` taşır, sorgular otomatik filtrelenir.
- `TenantId = null` → **host (global) kayıt**; tüm kiracılar okur (ör. ülkeler, coğrafya,
  pazaryeri kategori taksonomileri, kargo tarifeleri).
- **Tek fiziksel veritabanı**: kiracı başına ayrı DB yok; izolasyon sorgu filtresiyle.

## 2.2 Organizasyon ağacı: Şirket → Şube → Kasa
```
Tenant
 └── Company (Şirket)        — ülke + BaseCurrencyUnit (değerleme/bilanço birimi) + IsHeadquarters
      └── Branch (Şube)      — kendi base birimi olabilir (yoksa şirketinki)
           └── Vault (Kasa)  — fiziksel/mantıksal kasa
```
**Değişmezler (OrgTreeManager tarafından zorlanır):**
- Bir tenant en az bir **merkez (HQ)** şirketle doğar; tenant başına **tek HQ**.
- Şirket oluşturulunca otomatik bir HQ şube, şube oluşturulunca otomatik bir varsayılan
  kasa kurulur → **her seviye en az 1 çocuk taşır**.
- HQ silinmeden önce görevi başka bir kayda **devredilmelidir** (devir-önce-sil).

## 2.3 "Çalışma bağlamı" (working context) — KRİTİK
Kullanıcı oturumu bir **çalışılan şirket + çalışılan şube (+ kasa)** taşır. UI'da üst barda
seçilir.
- **Sunucu client'a güvenmez.** İstemcinin gönderdiği `CompanyId` **yok sayılır**; sunucu
  ortamdaki (ambient) çalışılan şirketle **ezer**.
- Çözümleme kuralı (`WorkingCompanyScope`):
  - Seçili şirket izinli kümedeyse → aynen kullanılır.
  - Değilse → **ilk izinli şirkete düşülür**, `null`'a DEĞİL. (`null` = konsolide = tüm
    tenant görünür = ters güvenlik.)
  - Hiç izinli şirket yoksa → **boş GUID sentinel**: sahipli (owned) kayıtlar görünmez,
    paylaşılan/host katalogları görünür kalır.
- Bu, "null-permissive filtre" tuzağına düşmemek içindir. **Aynen uygula.**

## 2.4 Sahiplik arayüzleri
| Arayüz | Anlamı | Örnek |
|---|---|---|
| `ICompanyOwned` | `CompanyId` **zorunlu** (nullable değil). Sahipsiz kayıt üretilemez. Güvenlik sınırıdır. | Metal, Stone, Jewelry, Good, Scrap, Future, Service, Product, SalesChannel, Voucher, Order, Account |
| `ICompanyScoped` | `CompanyId` **nullable** — null = tenant geneli. | EntityVariant, Media, ProductVariantDetail |
| (yok) | Host-global katalog, `TenantId` bile yok. | AdministrativeArea, Locality, N11City, N11Category, TrendyolCategory, EtsyTaxonomy, MarketplaceShipmentTariff, N11ShipmentCompany |

**TÜM emtia katalogları per-company'dir** (Metal · Stone · Jewelry · Good · Scrap · Future ·
Service). Benzersizlik anahtarı `(TenantId, CompanyId, Code)` + soft-delete farkındalıdır.
Sahiplik **istemciden değil çalışılan şirketten damgalanır**; şirket yoksa işlem **fail-closed**
(hata) olur. *(Cash, CurrencyUnit ve Country hâlâ host‖tenant kataloğudur — bunlar emtia değildir.)*

> **Tarihsel uyarı:** Erken tasarımda emtialar "host seviyesi, sahipsiz holding katmanı"
> olarak düşünülmüştü. Bu **yanlıştı** ve cross-company manipülasyonun taşıyıcısıydı.
> Yeniden yazımda o modele DÖNME.

## 2.5 İzinler
İki katmanlı:

**(a) Klasik izin ağacı** — `TradeXpress` grubu altında her varlık için `Default / Create /
Update / Delete` çocukları. Özel gruplar:
- `Reports.*`: Position, BalanceSheet, Transactions, Cash, Metal, Scrap, Good
- `Transactions.*`: Metal, Scrap, Cash, Convert, Service, Future, Stone, Jewelry, Good,
  Bullion, Assay, DebitNote, Transfer
- `Confirmations.*`: Propose, Declare, Confirm, Reject, View

**(b) Kapsamlı yetki (`UserScopedGrant`)** — bir kullanıcıya bir **rol** veya **doğrudan
izin**, belirli bir **Şirket/Şube/Kasa** kapsamında verilir.
- `RoleId` ve `PermissionName` **aynı anda dolu olamaz** (XOR).
- İkisi de boş olabilir → **saf coğrafi erişim** grant'ı (yalnız kapsam belirtir).
- Kapsam hiyerarşisi: `BranchId` doluysa `CompanyId` zorunlu; `VaultId` doluysa `BranchId` zorunlu.
- `Mode`: **Grant** / **Deny**. Çözümlemede **en spesifik kapsam kazanır** (cascade + dar override).

---

# BÖLÜM 3 — ORTAK ÇEKİRDEK KURALLAR (HER MODÜLDE GEÇERLİ)

Bunlar sistemin "fizik yasaları". Yeniden yazımda bunları **önce** kur.

## 3.1 Varlık (entity) konvansiyonları
- Tüm aggregate root'lar **GUID** kimlikli, **tam denetimli** (oluşturan/değiştiren/silen +
  zaman damgaları) ve **soft-delete**'lidir.
- **Id ve TenantId asla constructor parametresi değildir** (altyapı basar).
- Yazılabilir alanlar **korumalı setter** (`protected set`) + niyet belirten metotlarla
  değişir: `SetName()`, `SetActive(bool)`, `SetCode()`. **Serbest public setter yok.**
- **Aggregate'ler arası referans yalnız Id'dir** (navigation property yok). Aynı aggregate
  içindeki child → root bağı serbesttir.
- Kritik alanlar **set-once** (oluşturmadan sonra değişmez): sahiplik (`CompanyId`), kapsam
  (`BranchId`, `VaultId`), tip ayrımcıları, parent bağları.

## 3.2 Metin normalizasyonu (StringFieldGuard) — TEK KAYNAK
Her metin alanı merkezî bir muhafızdan geçer:
- **`NormalizeCode`**: Trim → çoklu boşluk tek boşluğa → **BÜYÜK HARF** (boşluk korunur) →
  zorunluluk/min/max doğrulaması.
- **`NormalizeName`**: Trim → çoklu boşluk tek boşluğa → **Başlık Biçimi (Title Case)** →
  doğrulama.
- **`EnsureOptionalText` / `EnsureRequiredText`**: uzunluk sınırları.
- **`EnsureRange`**: sayısal aralık.
- **Kültür tuzağı:** Türkçe `i/I` dönüşümü nedeniyle normalizasyon **kültür-bağımsız
  (invariant)** yapılmalıdır. Aksi halde "İstanbul" ≠ "ISTANBUL" olur.
- **Arama katlaması:** kullanıcı araması aksan/büyük-küçük harf duyarsız olmalı; ayrıca
  `"1"` ile `"01"` gibi kodların **farklı** kabul edildiği yerler vardır (kod eşleştirmede
  sıfır dolgusu **anlamlıdır**, sayıya çevirip karşılaştırma YAPMA).

## 3.3 Para ve sayı
- Tüm parasal/miktar alanları **decimal** (asla float/double).
- Kanonik ölçekler: **tutar N2**, **milyem/çarpan/kur N5**, **adet N5**.
- Kalıcılaşan tutar **kayıt anında N2 + AwayFromZero** yuvarlanır (`FinancialRounding`).
  Ara hesaplar **ham** kalır; yalnız yazılan değer yuvarlanır.
- İşaret konvansiyonu (defterde): **`+` = ALACAK / long**, **`−` = BORÇ / short**.

## 3.4 Zaman semantiği — İKİ AYRI TİP, KARIŞTIRMA
| Tip | Saklama | Görüntü | Örnek |
|---|---|---|---|
| **Zaman damgası** | **UTC** | Kullanıcının tarayıcı yerel saatine **merkezî** dönüşüm | `CreationTime`, `OrderDate`, `FetchedAt` |
| **İş tarihi (business date)** | **Wall-clock, dönüşümsüz** (`Kind=Unspecified`) | Olduğu gibi | `VoucherDate`, `DueDate`, indirim/üretim/son-kullanma tarihleri |

**İş tarihi alanları timezone kaydırmasına GİRMEZ** — gün kayması muhasebeyi bozar
(ay sonu fişi bir önceki aya düşer). `VoucherDate` saniye hassasiyetinde kesilir ve
Kind sabitlenir. Sayfa başına elle dönüşüm YAPILMAZ; tek merkezî dönüştürücü olur.

## 3.5 Hata yönetimi
- Ham platform istisnaları (`ArgumentException` vb.) domain'de **yasak**; tipli iş
  istisnaları kullan: `RequiredPropertyException`, `BusinessException("TradeXpress:<Alan>:<Kod>")`.
- Hata kodları lokalizasyon anahtarıdır → kullanıcıya kendi dilinde mesaj döner.
- **`catch {}` ile kök nedeni gizlemek, mock/sahte dönmek, uyarı bastırmak YASAK.**

## 3.6 Eşzamanlılık ve benzersizlik
- Optimistic concurrency damgası her aggregate'te.
- Benzersizlik kısıtları **veritabanı seviyesinde** + soft-delete farkındalı
  (silinmiş kayıt kodu bloke etmez).
- Kısıt ihlali yakalanıp **anlamlı iş hatasına** çevrilir ("Bu kod zaten kullanılıyor").

---

# BÖLÜM 4 — FİNANSAL ÇEKİRDEK (BİRİM, KUR, PARİTE, FİYAT)

## 4.1 CurrencyUnit (Para/Emtia Birimi)
Bir **fiyatlama ve kayıt birimi**. İki tip:
- `Cash` — nakit döviz (TRY, USD, EUR)
- `Metal` — kıymetli maden (HAS=has altın, GUM=gümüş, PLT=platin, PLD=paladyum)

Alanlar: `Code`, `Name`, `Type`, `IsActive`, `DisplayOrder`, `Description`,
`AlwaysShowInBalance` (bakiye listesinde her zaman görünsün mü).

**Kritik ayrım:** *Birim = kimlik. Fiyat burada DEĞİL.* Fiyat ayrı bir kur kaydında yaşar.

**Takip (follow) ilişkisi** — yapısal/global:
`FollowingUnitId` + `FollowingMargin` → "PLD, HAS'tan şu marjla türetilir".
Bu **herkes için aynıdır** (kimliğin parçası). **Tek seviye kuralı**: takip edilen birim
kendisi takip-eden olamaz (zincir yok). Birim kendini takip edemez.

**Marj (`CurrencyUnitMargin`)** — alış/satış marjı **per-tenant**, ayrı kayıtta.
Bir marj ayarının tipi (`MarginType`):
| Tip | Formül |
|---|---|
| `Multiply` | `final = market × Value` (Value=1 → değişiklik yok) |
| `Percent` | `final = market × (1 + Value/100)` |
| `Amount` | `final = market + Value` |
| `FinalPrice` | `final = Value` (beslemeyi yok say, sabit fiyat) |

Aynı marj mekanizması üç yerde kullanılır: besleme düzeltmesi (host), follow markup'ı,
bağımsız sabit fiyat.

## 4.2 ExchangeRate (Kur) ve canlı besleme
- Kurlar bir **bellek içi cache**'te canlı tutulur; periyodik olarak kalıcı anlık görüntü
  (snapshot) yazılır.
- Besleme kaynağı: **Harem Altın** sayfasından **headless tarayıcı (Playwright)** ile
  kazıma. Yapılandırma: `FetchInterval` (~5 sn), `PersistInterval` (~15 dk),
  `HaremFreshness` (~2 dk tazelik eşiği).
- Beslemeden gelen ham kotasyon (`MarketQuote`: alış/satış + yön) kod eşleme tablosundan
  geçip iç birim koduna çevrilir.
- **Fiyat hesabı (`CurrencyPriceCalculator`)**: piyasa fiyatı → marj uygula → follow
  zincirini çöz → **`ReBase`**: aktif şirketin/şubenin base birimine göre yeniden tabanla.

> **Yerel ≠ Bilanço birimi.** Kur **görüntüsü** kullanıcının yerel birimine, **pozisyon ve
> değerleme** ise bilanço (base) birimine re-base edilir. Karıştırma. Kullanıcı daima
> piyasa/alışık fiyatı görür; gerçek (base) değer arka planda hesaplanır.

## 4.3 Parity (Parite)
İki birim arasındaki kanonik çift: `Main (base)` + `Quote`.
- Görünen kur = `alış(base) / alış(quote)` (ör. USD/TRY = 45.59), **çağrı sırasından bağımsız**.
- Bir işlem satırında: ana birim paritenin **Main**'i ise **çarp**
  (`PayTotal = Amount × PayFactor`), değilse **böl** (`PayTotal = Amount / PayFactor`).
- Birim önceliği (`CurrencyUnitPriority`) hangi birimin base olacağını belirler.

---

# BÖLÜM 5 — CARİ HESAP VE KASA

## 5.1 Account (Cari Hesap)
Şirkete ait (`ICompanyOwned`), per-tenant.
- `Code`, `Name`, `Description`, `IsActive`
- **`BalanceCurrencyUnitId` — ZORUNLU**: bakiyenin tutulduğu birim
- **`Limit` + `LimitUnitId` — ZORUNLU**: kredi/risk limiti **ayrı birimde** tutulur
  (varsayılan şirketin bilanço birimi)

## 5.2 SubAccount (Alt Hesap)
- `AccountId` **zorunlu, set-once**
- `BranchId` **opsiyonel ama set-once** — null kaydedilmişse sonradan da atanamaz
- `CompanyId` parent hesaptan **denormalize** edilir (set-once)
  → *Neden:* alt hesap kendi şirket kolonunu taşımasa doğrudan silme çağrısıyla
  **cross-company IDOR** açığı kalırdı. Şirket filtresi bunu yapısal olarak kapatır.

## 5.3 Kasa bakiyeleri neden sahte cari üretmez
Bir fiş ya bir **cari hesaba** ya bir **iç kasaya** karşı yazılır. Bu ayrım `AccountType`
enum'u ile yapılır ve **şema tektir**:

| `AccountType` | `AccountId` neyi gösterir | `SubAccountId` neyi gösterir |
|---|---|---|
| `CurrentAccount` (0, **varsayılan**) | `Account.Id` | `SubAccount.Id` |
| `Vault` (1) | `Branch.Id` | `Vault.Id` |

Dört alan da **tipten bağımsız zorunludur**. Böylece okuma yolları (liste/ekstre/bakiye)
tipe bakmadan, sorgu imzası değişmeden çalışır ve **kasa bakiyeleri sahte cari hesap
üretilmeden ayrışır**. Kod alanları (`AccountCode`, `SubAccountCode`) **kayıt anında
dondurulan snapshot**'lardır — kaynak sonradan yeniden adlandırılsa fişin gösterdiği kod
değişmez.

Bu alanlar **id-only snapshot**'tır; tek kolon iki farklı tabloya işaret ettiği için
**yabancı anahtar kurulamaz** — bütünlük tip + guard ile korunur.

---

# BÖLÜM 6 — EMTİA KATALOGLARI

Hepsi **company-owned**, per-tenant, `Code`/`Name`/`Description`/`IsActive` ortak alanlı.

| Katalog | Ne ifade eder | Ayırt edici alanlar |
|---|---|---|
| **Metal** | İşlenmiş maden / sikke | `FollowingUnitId` (ZORUNLU, ör. HAS), `Factor` (milyem — gram-altı ≤1 ör. 0.995; sikkede birim-başı HAS-gram >1 ör. 1.605), `FactorChange`, `IsQuantity` (adet bazlı mı), `StableQuantity` (adet başına gram), tek temsili görsel |
| **Scrap** | Hurda maden | Metal'e benzer; **işçilik takibi yok** |
| **Future** | Vadeli işlem | takip birimi + faktör |
| **Service** | Hizmet (gider/gelir) | — |
| **Stone** | Değerli taş | parasal/adet; **milyem/işçilik yok**; `Category` (SpecialCode) |
| **Jewelry** | Bitmiş mücevher | parasal/adet; `Category` |
| **Good** | Genel ticari mal (kuyumculuk dışı) | parasal/adet; tedarikçiler (`GoodSupplier`), varyant detayları |
| **Cash** | Nakit enstrümanı | host‖tenant kataloğu (company-owned DEĞİL) |
| **AssayOffice** | Ayar evi / rafineri laboratuvarı | **Emtia değil**, tüzel taraf — organizasyon kuşağında |
| **AddOn** | Sipariş anı fiyatlı seçenek (kurdele/kutu/ambalaj) | ürünlere atanır |
| **SpecialCode** | Agnostik gruplama kodu | tek tablo tüm entity'lere hizmet eder |

**Adlandırma (Türkçe ERP terimi → İngilizce identifier):**
Bilanço→BalanceSheet · Cinsi→Category · Bakiye→Amount/Balance · Takoz→Bullion ·
Pırlanta/Mücevher→Jewelry · Taş→Stone · İşçilik→Labor · Çeşni→Alloy/Assay · Şube→Branch ·
Kasa→Vault · Hesap→Account · Mamül→Good · Hurda→Scrap · Milyem→Factor · Fiş→Voucher.

**Identifier'lar İngilizce, yorumlar Türkçe** (mevcut konvansiyon; yeni platformda dil
tercihini sahibine sor).

---

# BÖLÜM 7 — AGNOSTİK VARYANT SİSTEMİ

Tek bir varyant altyapısı **tüm** entity'lere hizmet eder (Product, Good, Metal…).

```
EntityAttribute        (nitelik/eksen: "Renk", "Beden")  → EntityName + EntityId ile sahibe bağlı
  └── EntityAttributeValue   ("Kırmızı", "42")
EntityVariant          (kombinasyondan doğan varyant)
  └── EntityVariantAttributeValue  (varyant ↔ değer bağı)
```

**Kurallar:**
- `EntityName` + `EntityId` **set-once**; sahip entity başına nitelik sayısı sınırlıdır.
- Varyantlar **değer kartezyeninden** üretilir (`VariantCombinationEngine`).
- Varyant `Code`/`Name` kombinasyondan **otomatik türetilir**.
- Sahip entity başına **tekil `IsMain`** (ana varyant) — en az 1 varyant değişmezi.
- Nitelik grafı değişince varyant kümesi **uzlaştırılır** (`VariantSetReconciler`):
  yeni kombinasyon eklenir, geçersizleşen pasifleşir, **mevcut varyanta bağlı zengin veri
  (fiyat/reçete) korunur**.
- **Zengin, entity-özel alanlar ayrı uzantı tablosundadır** (`ProductVariantDetail`,
  `GoodVariantDetail`, `MetalVariantDetail`) — çekirdek varyant onları bilmez, sahip
  servis eşleyip yükler.
- `VariantTemplate`: yeniden kullanılabilir nitelik demeti kataloğu; ürüne "katalogdan
  uygula" ile aktarılır.

---

# BÖLÜM 8 — İŞLEM MOTORU: FİŞ (VOUCHER) VE BAKİYE DEFTERİ

**Sistemin kalbi burasıdır.** En büyük dikkati bu bölüme ver.

## 8.1 Voucher (Fiş) — başlık
Company + Branch (+ opsiyonel Vault) kapsamlı, per-tenant.
- Tüm kapsam alanları (**Company/Branch/Vault/AccountType/Account/SubAccount**) **set-once**.
- `VoucherNumber`: **şirket bazında otomatik artan** uzun sayı (numara verme kararı ayrı
  serviste; entity yalnız atamayı kabul eder).
- `VoucherDate`: **kullanıcı girişi**, `CreationTime`'dan bağımsız, saniye hassasiyetinde,
  **wall-clock** (bkz. §3.4).
- `Description` opsiyonel.
- **Bir fiş = tek karşı taraf** (tek cari ya da tek kasa). Satırlar bu başlığın altındadır.
- Satır ekleme/güncelleme/silme aggregate metotlarıyla; **silme soft-delete**'tir
  (satır DB'de kalır, aktif kümeden düşer).

## 8.2 VoucherLine (Fiş Satırı) — iki bacaklı takas modeli
Her satır bir **takas**tır: bir şey verilir, karşılığında bir şey alınır.

**Ana bacak:**
`CommodityId` + `CommodityCode` (snapshot) · `VariantId`+`VariantCode` (opsiyonel snapshot) ·
`Quantity` (adet, N5) · `Amount` (miktar, N2) · `Factor` (milyem/çarpan, N5 — nakitte 1) ·
`Total` (genelde `Amount × Factor`, N2) · `MainUnitId`

**Karşılık bacağı:**
`PayFactor` (satış/ödeme fiyatı ya da parite, N5) · `MarketPrice` (o anki piyasa fiyatı
snapshot'ı) · `PayTotal` (tahsil/tediye tutarı, N2) · `Profit` (kâr, TL, yön bağımsız) ·
`PayCommodityId`+`PayCommodityCode` · `PayUnitId` · `PayUnitRate` (karşılık biriminin
işlem anındaki **alış kuru snapshot'ı**)

**Ortak:** `Type` (ProcessType) · `Direction` · `PaymentType` · `DueDate` (date-only,
wall-clock) · `Description` · soft-delete bayrağı

**Sınıflandırma (`Type`) ve kapsam (`VoucherId`) satırda değişmez.**

Emtia referansları **FK değil snapshot**'tır: katalog kaydı silinse bile fiş okunabilir kalır.

## 8.3 ProcessType — işlem türleri
| Değer | Ad | Anlamı |
|---|---|---|
| 1 | `Metal` | Maden alış-satış (milyem + işçilik) |
| 2 | `Scrap` | Hurda maden |
| 3 | `Cash` | Nakit giriş/çıkış |
| 4 | `Convert` | Çevrim (bakiye/birim çevirme) |
| 5 | `Service` | Hizmet (gider/gelir) |
| 6 | `Future` | Vadeli işlem |
| 7 | `Stone` | Taş (parasal/adet; milyem-işçilik yok) |
| 8 | `Jewelry` | Mücevher (parasal/adet) |
| 9 | `Good` | Mamül / genel ticari mal |
| 11 | `Transfer` | Virman — hesaplar arası aktarım (satır-seviyesi karşı kayıt) |
| 14 | `Assay` | Çeşni — biriken çeşni stoğundan cariye metal verme. **Yön daima ÇIKIŞ.** |
| 15 | `Bullion` | Takoz (külçe) giriş/çıkış |
| 99 | `DebitNote` | Borç/Alacak dekontu — kategorili serbest tutar; **Miktar alanı yok** |

## 8.4 ProcessDirectionType — yön
| Değer | Ad | Bakiye yönü |
|---|---|---|
| 0 | `Inbound` (Giriş) | + |
| 1 | `Outbound` (Çıkış) | − |
| 2 | `Credit` (Alacak) | + |
| 3 | `Debit` (Borç) | − |
| 4 | `Buy` (Alış) | + |
| 5 | `Sell` (Satış) | − |

**Tek kaynak kural: `IsInflow = (int)Direction % 2 == 0`.** Çift değerler giriş, tek
değerler çıkıştır. Bu kuralı bozacak yeni değer ekleme.

UI combo'su `ProcessType`'a göre alt küme gösterir:
Nakit/Maden/Hurda → Giriş/Çıkış · Çevir → Alacak/Borç · Vadeli → Alış/Satış.

## 8.5 ProcessPaymentType — ödeme tipi
| Değer | Ad | Bakiyeye etkisi |
|---|---|---|
| 0 | `Normal` | Yansır (veresiye/hesaba) |
| 1 | `WithCash` (Peşin) | **Yansımaz** (anlık ödeme) |
| 2 | `WithCurrency` (Bedelli) | Yalnız bedel bacağı yansır |
| 3 | `Return` (İade) | Normal gibi |
| 4 | `Consignment` (Emanet) | Normal gibi |
| 5 | `WithUnit` (Birim bazlı) | — |
| 6 | `Reservation` (Rezervasyon) | **Yansımaz** — fiziksel stok hareketi yaratmaz; yalnız kullanılabilir stoğu düşüren taahhüt sayacı. Kapanma elle. |

Bazı işlem türlerinde ödeme tipi hiç yoktur → `null`.

## 8.6 İşlem kısa kodu (grid'deki "İşlem" kolonu)
Harflerin birleşimi: `ProcessType` + `Direction` + `PaymentType`.

*Tip harfleri:* Metal=M · Scrap=H · Cash=N · Convert=C · Service=G · Future=V · Stone=T ·
Jewelry=J · Good=**U** (mam**U**l) · Transfer=V · Bullion=T · DebitNote=D
*Yön harfleri:* Inbound=G · Outbound=C · Credit=A · Debit=B · Buy=A · Sell=S
*Ödeme harfleri:* Normal=N · WithCash=P · WithCurrency=B · Return=I · Consignment=E ·
WithUnit=M · Reservation=R

**Özel yollar:** Bullion → sabit `"TGA"` (giriş) / `"TCA"` (çıkış). Assay → `"C"`.
Convert ve Future ödeme harfi **almaz**.

*Çakışma analizi (koru):* Taş ve Takoz ikisi de "T" ile başlar ama takozun 3. harfi daima
"A" ve hiçbir ödeme tipi "A" üretmez → ayrışma garantili (Taş Giriş Normal = "TGN" ↔
Takoz Giriş = "TGA"). Transfer ve Future "V" paylaşır ama Future 3. harf üretmez →
"VA"/"VS" ↔ "VGN"/"VCN".

## 8.7 Satır hesap motoru (`VoucherLineCalculator`) — SAF FONKSİYON
Altyapısız, UI-agnostik. **Aynı motoru hem istemci hem sunucu çağırır** → tek kaynak,
UI'da karar yok. Dış bağımlılıklar **delege** ile enjekte edilir:
- `buyRateOf(unitId) → TL alış kuru` (TRY→1; bilinmiyorsa 0)
- `parityMainOf(unitA, unitB) → paritenin Main birimi` (kayıt yoksa null)

Davranış:
- `Cash`, `Convert`, `Future`, `Scrap`, `Metal` → aynı parite matematiği
  (peşin/bedelli pay bacağı). Diğer ödeme tiplerinde panel kendi hesabını yapar.
- `Bullion`, `Assay` → **passthrough** (parite pay-bacağı yok; çok-metalli ayrı hesap).
- Ödeme tipi `WithCash` ise karşılık kaynağı **nakit enstrümanı**, değilse **birim**.

## 8.8 BAKİYE DEFTERİ (Ledger) — MİMARİNİN OMURGASI

### Model
```
VoucherLine → [ilgili Poster] → 0..N BalanceEffect(UnitId, Amount) → 0..N BalanceLedgerEntry
```
- **`BalanceEffect`**: `(UnitId, Amount)` — işaretli net etki. `+` alacak, `−` borç.
  Bir satır **birden çok birimi** etkileyebilir (iki bacaklı işlemler).
- **`BalanceLedgerEntry`**: poster çıktısının **kalıcı** kaydı. Fişin kapsamını
  (Company/Branch/Vault + AccountType + Account/SubAccount + kod snapshot'ları) ve satırın
  sınıflandırmasını (ProcessType/Direction/PaymentType/VoucherNumber/VoucherDate) **kopyalar**.
  Tutar kayıt anında N2 yuvarlanır.

### Poster mimarisi — HARDCODE YOK
Her `ProcessType` için **tek bir poster** vardır; hepsi aynı arayüzü uygular:
```
interface IVoucherLineBalancePoster {
    ProcessType ProcessType { get; }
    IEnumerable<BalanceEffect> Post(VoucherLine line);
}
```
Hesaplayıcı tüm posterları **DI ile otomatik toplar**. Poster'ı olmayan tip bakiyeyi
etkilemez (sessizce atlanır). **Yeni işlem türü = yalnız yeni poster yaz.**

### Senkronizasyon
Fiş kaydet/güncelle/sil işlemlerinde, o `VoucherId` için ledger satırları
**silinir + yeniden yazılır**. Böylece defter kaydedilen işlemle **inşaen tutarlıdır**;
kısmi güncelleme kaçağı olamaz.

### Poster kuralları (ZORUNLU DAVRANIŞ TABLOSU)

**Ortak:** `sign = IsInflow ? +1 : −1`

| Poster | Kural |
|---|---|
| **Metal** | Peşin/Rezervasyon → **etki yok**. Bedelli → **tek bacak**: `(PayUnitId, sign × PayTotal)` (işçilik Factor'a yedirildiği için Total bedele zaten yansır). Normal/İade/Emanet → **İKİ bacak**: `(MainUnitId, sign × Total)` + `(PayUnitId, sign × PayTotal)` (işçilik bacağı). |
| **Scrap** | Metal'e benzer, farkı: **Normal'de işçilik ikinci bacak olarak yansımaz**. |
| **Cash** | Peşin/Rezervasyon → etki yok. Aksi halde nakit bacağı. |
| **Convert** | İki bacaklı çevrim (bir birimden çıkar, diğerine girer). |
| **Service** | Hizmet bedeli tek bacak. |
| **Future** | Vadeli pozisyon bacağı. |
| **Stone / Jewelry / Good** | Parasal/adet — Normal'de tutar bacağı; peşin/rezervasyon etkisiz. |
| **Transfer** | Virman: **çift bacak, zıt yönlü, ortak `LinkId`** ile bağlı (aşağıya bak). |
| **Assay** | Çeşni çıkışı — yön daima çıkış. |
| **Bullion** | Çok-metalli — ana metal + yan metaller + işçilikler, her biri kendi dağıtım moduna göre (aşağıya bak). |
| **DebitNote** | Serbest tutar borç/alacak. |

> Yeniden yazımda her poster için **birim testi** yaz: her ödeme tipi × yön kombinasyonu
> için beklenen `BalanceEffect` listesi. Bu, sistemin en kırılgan yeridir.

## 8.9 Virman (Transfer) mekaniği
- Satır `CounterAccountId` taşır (**karşı taraf alt hesabı**, id-only).
- **Fiş = tek cari** kuralı gereği karşı bacak, o alt hesabın **kendi fişinde** açılır.
- İki zıt yönlü satır aynı **`LinkId`**'yi taşır; güncelleme/silme ikizini bu kimlikle bulur.
- Karşı hesap **satır başına** seçilir (aynı ürün farklı siparişte farklı karşı tarafa —
  ör. kargo firmasına — gidebildiği için).

## 8.10 Takoz (Bullion) — en karmaşık işlem
Bir külçenin ayar evine verilip/alınmasıdır. **Dört metal aynı satırda** işlenir.

**Takoz tipi (`BullionType`)** ve kanonik ana birimi:
Gold=1→HAS · Silver=2→GUM · Platinum=3→PLT · Palladium=4→PLD

**Satır alanları:**
- Ana metal: `Factor` = altın milyemi @ `MainUnitId`; altın işçiliği = `PayFactor` @ `PayUnitId`
- Yan metal milyemleri: `SilverFactor`, `PlatinumFactor`, `PalladiumFactor`
- Yan metal işçilik oranları ve işçilik birimleri (dördü ayrı)
- Yan metal bacak birimleri (gümüş/platin/paladyum bakiyesi hangi birime postlanır)
- Ayar evi bağı: `AssayOfficeId`, `ReportNo`, `IsReport`, `IsExtra`
- `AssayAmount` — **çeşni numune miktarı** (girişte cari bakiyeye dahil)
- **Kur snapshot'ları** (`GoldRate`, `SilverRate`, …, `*LaborUnitRate`): kayıt anında
  dondurulur → **poster ek kur okuması YAPMAZ**

**Yan metal dağıtımı (`MetalDisposition`)** — her yan metal için ayrı seçilir:
| Değer | Ad | Davranış |
|---|---|---|
| 0 | `Deliver` (Madeni Ver) | Metalin **kendi biriminde** bakiye: `Miktar × Milyem` |
| 1 | `ConvertToGold` (Altına Çevir) | Kur üzerinden HAS bakiyesine: `değer × MetalKur / HasKur` |
| 2 | `DeductFromLabor` (İşçilikten Düş) | Kur üzerinden **işçilik borcundan** düşülür |
| 3 | `Keep` (Madeni Bırak) | **Bakiyeye yansımaz** — metal dükkânda kalır |

**İşçilik tahsil şekli (`BullionLaborMode`)**:
| Değer | Ad | Sonuç |
|---|---|---|
| 0 | `DeductFromGold` (Altından Düş) | İşçilik **HAS** cinsinden borçlanır → `PayUnitId` = HAS |
| 1 | `WithCash` (Para İle) | İşçilik **seçilen para biriminde** borçlanır → `PayUnitId` = o birim |

Bacak matematiği ayrı bir saf hesaplayıcıda (`BullionLegCalculator`) yaşar.

## 8.11 Satır geçmişi
`VoucherLineHistory` — satırın her değişikliği (`VoucherLineChangeType` + `EditedField`
kümesi) kaydedilir. Kullanıcı bir satırın kim tarafından ne zaman nasıl değiştiğini görebilir.

---

# BÖLÜM 9 — TEYİT (CONFIRMATION): ZERO-TRUST İÇ TRANSFER

Organizasyon **içi** (kasa ↔ kasa) transferlerde **karşılıklı ayna onayı** mekanizması.

## Neden var
Bir kasadan diğerine mal/para geçerken tek taraflı beyan karşı tarafın defterini
kımıldatmamalıdır. **Sistem karşı kaydı otomatik aynalamaz** — her taraf kendi gerçeğini
**kendi eliyle** yazar. Böylece kimse kimsenin kaydını onun yerine yazamaz; herkes kendi
kaydını sahiplenir ve sonradan inkâr edemez.

## Akış
```
1. PROPOSE  — Gönderen normal işlem panelinde kaydı girer.
              Karşı taraf iç kasa olduğu için HEMEN POSTLANMAZ;
              Confirmation(Proposed) doğar. İzin: Confirmations.Propose
2. DECLARE  — Alıcı KENDİ girişini kendi eliyle oluşturur.
              (sistem türetmez — iki bağımsız beyan)   İzin: Confirmations.Declare
3. CONFIRM  — Gönderen teyit eder → ANCAK O AN iki ayna bacak
              (gönderen −, alıcı +) ATOMİK postlanır.  İzin: Confirmations.Confirm
   REJECT   — Alıcı reddedebilir → süreç durur.        İzin: Confirmations.Reject
```

## Kurallar
- **İki payload**: `InitiatorPayloadJson` + `CounterpartyPayloadJson` — her taraf kendi
  satırının tam serileştirilmiş halini kendi payload'unda taşır.
- **Denormalize ayna anahtarı** (`ConfirmationMirrorKey`): emtia · varyant · miktar · tutar ·
  ana birim · karşılık birimi · **ZIT yön**. Sorgu/grid ve karşılaştırma bunun üzerinden.
- Ayna tutmazsa **fark ekrana düşer** → fire/kayıp dedektörü olarak çalışır.
- **İptal yoktur**: gönderen teklifi geri çekemez; süreci yalnız alıcı reddederek durdurur.
- Değer, teyit kapanana dek **gönderenin sorumluluğundadır**.
- `Confirmation` company-owned; kasa referansları id-only.

## UI
- **Teyitler** menüsü: gelen/giden kutusu (grid + durum filtresi).
- **Transferler** ekranı **ayrı bir form DEĞİLDİR** — Cari İşlemler ile **aynı** fiş
  formunun organizasyon-içi kipidir (Cari Hesap→Alt Hesap yerine Şube→Kasa). Kipi
  **menü/rota** belirler; formda karşı-taraf combo'su yoktur.

---

# BÖLÜM 10 — ÜRÜN, REÇETE, FİYATLAMA VE MUADİL

## 10.1 Product (Ürün)
Satılabilir **kanonik, polimorfik emtia** (Nakit hariç her aile). Company-owned.
Ürün bir **vitrin + gruplamadır**; satılabilir asıl bilgi (fiyat/reçete/görsel)
**varyantlarda** yaşar.

**Kimlik/temel:** `Code`, `Name`, `Description`, `ProductCategoryId` (opsiyonel),
`IsActive`, `Images` (owned JSON listesi; sıra `DisplayOrder`, ilk = ana görsel)

**Pazaryeri-genel varsayılanlar** (kanal-ürünü bunları **devralır**, sonra override edebilir):
- `OriginCountryId` — **menşei ülke**. N11'in beklediği `domestic` bayrağı buradan
  **türetilir** (menşei == şirketin ülkesi). *Not: eskiden elle işaretlenen bir `Domestic`
  bayrağıydı; ülke gerçek veri olduğu için bayrak ondan hesaplanır hâle getirildi.*
- `Condition` (ProductCondition: New/…)
- `WhoMade`, `MadePeriod` (19 kovalı kronolojik), `IsSupply` — **Etsy zorunlu menşe alanları**
- `PreparingDay` (kargoya verilme süresi, ≥1, varsayılan 1)
- `MaxPurchaseQuantity`, `SellerNote`, `CurrencyUnitId`
- `PackageDesi` — kargo tarifesinin girdisi. **Çözüm sırası dardan genişe:
  varyantın desisi → ürünün desisi → kanalın varsayılanı.** `0` geçerlidir ("Dosya"
  basamağı). Doğruluğu kullanıcının sorumluluğundadır (fiyatlama içindir; gerçek kargo
  bedeli sipariş sürecinde netleşir).
- `SpecialInfo` (owned JSON) — **ürün özelleştirme alanları**: müşterinin sipariş anında
  dolduracağı adlandırılmış alanlar. N11 `SpecialInfo` (SOAP) ve Etsy'nin **çoklu
  adlandırılmış kişiselleştirme sorusu** modeline 1:1 gider. *Etsy'nin eski tek-kutulu
  kişiselleştirme modeli kapandığı için tek taşıyıcı budur.*
- `AddOns` (owned JSON) — katalogdan seçim + satır override
- İndirim: `DiscountType` (None/Amount/Percentage) + `DiscountValue` + başlangıç/bitiş
  **iş tarihleri**
- `ProductionDate`, `ExpirationDate` (iş tarihleri)
- `RecipeTemplateId` — uygulanan reçete şablonu **ürüne kaydedilir**
  *(Neden: muadil motoru stok değişince kombinasyonları yeniden üretir ve her kombinasyonun
  reçetesini sıfırdan kurar. Şablon yalnız formun ömrü boyunca yaşasaydı yeniden üretilen
  varyantlara paketleme/kargo/sigorta satırları sessizce düşerdi.)*
  Bağ **id-only**'dir: şablondaki sonraki değişiklik **geçmiş satırları geriye dönük ezmez**.

**Varyant modu (`ProductVariantMode`):**
| Değer | Ad | Davranış |
|---|---|---|
| 0 | `MultiVariant` | **Varsayılan/statüko** — nitelik × değer kartezyeninden varyant üretilir |
| 1 | `SingleVariant` | Nitelik-tabanlı üretim kapalı; ürün tek ana varyantla yaşar |
| 2 | `Substitution` | Muadil (paket) — reçete, muadil grubu kombinasyon hesabından üretilir |
| 3 | `FromCatalog` | Nitelikler `VariantTemplate` katalogundan seçilir; üretim mekaniği MultiVariant ile **aynı** — ayrı mod olmasının sebebi *niteliklerin kaynağını* baştan seçtirmek |

> **Enum tasarım kuralı:** varsayılan/statüko davranış daima `0` değerindedir → mevcut
> satırlar migration default'u ile davranış değiştirmez.

**Stok politikası (`ProductStockPolicy`):**
| Değer | Ad | Davranış |
|---|---|---|
| 0 | `Fixed` | **Statüko** — stok elle girilir; orkestratör dokunmaz |
| 1 | `Calculated` | Satılabilir adet **reçete + eldeki maden stoğundan** türetilir (aşırı satış koruması) |
| 2 | `Unlimited` | Stok kısıtı yok; kanala daima "stokta var" gider |

Muadil ürünler **zorunlu olarak** `Calculated`'dır.

## 10.2 Reçete (`ProductVariantRecipeLine`)
Varyantın **design-time maliyetini** oluşturan bileşenler. **Deftere YAZMAZ** — yalnız
maliyet çıkarır.

**Kritik: net/tutar alanı YOKTUR.** Satır tutarı ve net maliyet **canlı hesaplanır**
(`ProductRecipeCostCalculator`): kur değişince maliyet güncellenir, **dondurulmaz**.
Katalog referansı FK'sız snapshot'tır; adet→gram çevirimi (`StableQuantity`) ve parasal
giriş fiyatı hesap anında katalogtan **canlı** okunur. Milyem (`Factor`) ise
**düzenlenebilir snapshot**'tır (milyem fiziksel özellik; canlı olan kurdur).

**Bileşen türü (`RecipeComponentType`):**
| Değer | Ad | Anlamı |
|---|---|---|
| 1 | `CatalogCommodity` | Fiziki katalog: Metal/Scrap/Future (metal-bacaklı, milyem×miktar) ya da Jewelry/Stone (parasal, giriş fiyatı×miktar). Aile `CommodityProcessType` ile taşınır. |
| 2 | `Service` | Hizmet — **devralınan taban** üstüne **türevsel bedel** (komisyon/sigorta/kargo) |

**Türev (derived) mekaniği** — hizmet satırında:
- `RecipeDerivedBaseMode`: taban ne? (tüm üst satırlar / seçili kalemler)
- `RecipeDerivedOperation`: işlem ne? (yüzde / **brütleştir (gross-up)** / …)
- Satır **yalnız kendinden ÖNCEKİ satırları** referanslar → **döngüsüz + deterministik**.
- Satır maliyeti = uygulanan bedel; net'e bu eklenir.

Satır alanları: `LineOrder` (kullanıcı sıralaması korunur), `Quantity`, `Amount`, `Factor`,
`ValuationUnitId` (doğal/rebase birimi), `PaymentType` (reçetede yalnız Normal ve
Bedelli anlamlı), `PayFactor` (Normal'de işçilik oranı; Bedelli'de 1 ana-birim başına bedel),
`PayUnitId`, `ManualAmount`+`ManualUnitId` (hizmet/manuel sabit tutar), `RecipeLineOrigin`
(satır nereden geldi: elle / şablon / yan-maliyet / muadil).

## 10.3 Fiyat türetme — TEK KAYNAK
```
fiyat = NetCost × (1 + Margin/100)      [MARKUP]
```
`Margin` null → marjsız (×1). **Yuvarlama yapılmaz** (ham değer döner). Bu formül
sunucu ve istemcide **aynı tek fonksiyondan** okunur; kopya formül yazmak yasaktır.

## 10.4 Yan maliyetler (`SideCostSettings`) — kanal gideri
Kanal üzerinde owned JSON. Sabit alanlı form değil, **gider satırları listesi**
(`SideCostItem`): tür (`SideCostKind`: Commission/Packaging/Shipping/Insurance/…) +
hesaplama modu (`SideCostCalcMode`) + değer + fiş hedefi (`SideCostPostingMode`) +
`IsEnabled` + `AutoRate` bayrağı + `DisplayOrder`.

**`SideCostRecipeComposer`** bu satırlardan kanal varyant reçetesine **otomatik satırlar**
üretir — idempotent uzlaştırma, **gross-up satırları HEP EN SONDA**.

**Gross-up matematiği ve guard:**
Aynı satış fiyatının yüzdesi olan kalemler **toplanıp tek satır** üretir:
`P = taban ÷ (1 − Σoran/100)`. Toplam oran payda sınırını (100) aşamaz → aşarsa **fail-fast**.
Ayrıca **aktif `AutoRate` kalemi en fazla 1 olabilir** (ikincisi aynı oranı sessizce iki kez
saydırırdı).

**Kanal başına varsayılan tohum farklıdır, model tektir:**
- **N11**: komisyon **kategoriden otomatik** (`AutoRate` işaretli satır; `Value` = fallback)
- **Trendyol**: ilk fazda kanal-oranı
- **Etsy**: kanal-sabit **%9,5 + $0,45/satış (USD)** + opsiyonel **Offsite Ads** (ayrı
  gross-up satırı, varsayılan kapalı)

**Eski şema toleransı:** JSON kolonundaki eski sabit-alanlı payload okumada satır listesine
**dönüştürülür** (kullanıcı test verisi kaybolmaz); yazım hep yeni şemadır.

## 10.5 Reçete şablonu (`RecipeTemplate`) — "orta reçete"
Yeniden kullanılabilir ara masraf demeti (paketleme / kargo / sigorta / hizmet /
yarı mamul). Başlık + sıralı satırlar. Ürüne uygulanır; uygulanmış satırlar **ürünün kendi
malıdır**.

## 10.6 Muadil (Substitution) motoru
**İş senaryosu:** "12 gram ziynet sepeti" satılıyor. Elde farklı gramajlarda ziynet
varyantları var. Sistem hedef gramajı karşılayan **kombinasyonları** üretir ve her
kombinasyonu bir ürün varyantı + reçetesi yapar.

**Bileşenler:**
- `SubstitutionGroup` (başlık) + `SubstitutionGroupItem` (sıralı emtia satırları) —
  ayrı aggregate, id-only referans, company-owned.
- Grup satırlarında **kapsam** (`IncludedVariantIds`) tanımlanabilir.
- Ürün seviyesinde **override** (`SubstitutionOverrideVariantIds`) — boş liste = gruptan
  devral; dolu ise grup kapsamını **tamamen ezer**.
- **Resolver zinciri: `override ?? included ?? ana varyant`**
- Hedef: `SubstitutionTargetQuantity` (gram, > 0, zorunlu)
- Tolerans: `ToleranceType` + değer — ürün seviyesinde override edilebilir
  (**tür ve değer ÇİFT dolar**, biri olmadan diğeri geçersiz)
- `SubstitutionVariantMode`: Single / Multiple — kombinasyonların varyanta dönüşme biçimi
- `SubstitutionReasonCodes`: bir kombinasyonun neden elendiği (tolerans dışı, stok yok…)

**Çözücü (`SubstitutionSolver`)** kombinasyonları üretir → **`SubstitutionStockItemPlanner`**
her kombinasyon için stok kalemi planı çıkarır → **`SubstitutionVariantMaterializer`**
bunları gerçek varyant + reçete satırlarına dönüştürür.

**Üretim otomatiktir:** ürün kaydında ve **maden stoğu değiştiğinde** yeniden çalışır.

## 10.7 Ürün orkestrasyonu (stok/fiyat senkronu)
- **`MetalStockReaderService`**: bakiye defterinden eldeki maden stoğunu okur.
- **`RecipeMetalReverseIndex`**: hangi maden hangi ürünlerin reçetesinde geçiyor
  (ters indeks).
- **`SellableStockCalculator`**: reçete + eldeki stoktan **satılabilir adet** hesaplar.
- **`ProductOrchestrationManager`**: maden stoğu değişince (`MetalStockChangedEto` olayı)
  etkilenen ürünleri bulur, stoğu yeniden hesaplar, kanallara iter.
- **`RepricingCycleWorker`**: periyodik yeniden fiyatlama döngüsü
  (`RepricingCycleElapsedEto`).
- **`ProductStockSyncJob`**: kanal stok güncelleme işi.
- **`IChannelStockPusher`**: kanal-agnostik stok itme arayüzü.
- **Aşırı satış (oversell) koruması** açıkça test edilmelidir.

---

# BÖLÜM 11 — ÜRÜN KATEGORİLERİ VE PAZARYERİ EŞLEŞTİRMESİ

## 11.1 Çekirdek taksonomi
`ProductCategory` — **serbest derinlikli ağaç**, company-owned.
`ProductCategoryAttribute` (nitelik) + `ProductCategoryAttributeValue` (değer).
Nitelik türü: `ProductCategoryAttributeKind`.
**Nitelikler ağaçta miras alınır** — alt kategori üstünkileri devralır
(`ProductCategoryEffectiveAttribute` efektif kümeyi çözer).

## 11.2 Neden var (dört kazanç)
Ürün bir kez çekirdek kategoriye bağlanınca:
1. Her satış kanalında kategori **ayrı ayrı seçilmez** — kanal kategorisi eşleştirmeden çözülür.
2. Kanal nitelikleri **elle doldurulmaz** — kategori nitelikleri ön-doldurur.
3. Kanalın **kategori komisyonu** ürün seviyesinde bilinir ve reçeteye gross-up maliyet
   olarak girer — **kanal ürünü hiç oluşturulmamış olsa bile fiyat hesaplanabilir**.
4. Tek yerden bakım.

## 11.3 Eşleştirme köprüsü
```
ProductCategoryChannelMapping                 (kategori ↔ kanal kategorisi + komisyon)
 └── ProductCategoryChannelAttributeMapping   (nitelik ↔ kanal niteliği)
      └── ProductCategoryChannelAttributeValueMapping  (değer ↔ kanal değeri)
```

> **Terminoloji yasağı:** Pazaryeri kanal-ürün entity'sinde **"Listing" kelimesi
> KULLANILMAZ**. Desen: `SalesChannelTr{Pazaryeri}Product`
> (`SalesChannelTrN11Product`, `SalesChannelTrTrendyolProduct`, `SalesChannelEtsyProduct`).
> Push metotları: `PushTo{Pazaryeri}Async`.

---

# BÖLÜM 12 — SATIŞ KANALLARI VE PAZARYERİ ENTEGRASYONU

## 12.1 Kanal modeli (TPT — Table Per Type)
```
SalesChannelBase (soyut)  — ortak: Code, Name, Description, IsActive, CompanyId,
                             SideCosts (owned JSON), DefaultPackageDesi (varsayılan 1),
                             SubAccountId (muhasebe hedefi)
 ├── SalesChannelTrN11        — AppKey + AppSecret
 ├── SalesChannelTrTrendyol   — SellerId + ApiKey + ApiSecret
 └── SalesChannelEtsy         — Keystring + SharedSecret + OAuth 2.0 PKCE token'ları
```
`SalesChannelType`: TrN11=1 · TrTrendyol=2 · Etsy=3
*(Etsy'de ülke öneki YOK — global platform; ülke yalnız mağaza konumudur.)*

**Muhasebe hedefi:** Kanal, pazaryeriyle olan hesabımızdır (komisyon borcu, hakediş
alacağı). `SubAccountId` **kullanıcının kendi cari planından** seçilir — **sistem cari
üretmez**. Cari hesap ayrıca tutulmaz (alt hesap zaten bir cariye bağlıdır; ikisini
saklamak çelişme riski açardı).

## 12.2 Kanal sağlama (provisioning)
`IChannelProvisioner` + kanal başına uygulama (`N11ChannelProvisioner`,
`TrendyolChannelProvisioner`, `EtsyChannelProvisioner`). Kanal kurulumunda adım adım
(`StepOutcome`) yürüyen bir sihirbaz: kimlik doğrula → referans verileri (kategori/şehir/
kargo firması) senkronla → varsayılan yan maliyetleri tohumla.
Kimlik doğrulayıcılar ayrıdır: `IN11CredentialVerifier`, `ITrendyolCredentialVerifier`,
`IEtsyCredentialVerifier`.

## 12.3 N11 entegrasyonu
- **Protokol: SOAP/XML.** Uç: `https://api.n11.com/ws/ProductService.wsdl`
- Kimlik: her istekte `appKey` + `appSecret` (auth bloğu). **Secret ASLA loglanmaz.**
- `ProductRequest` **WSDL `xs:sequence` sırasında** serileştirilmelidir (sıra bozulursa
  reddedilir). Prefix'li wrapper + niteliksiz (unqualified) çocuklar — kanıtlanmış desen.
- Yanıt **namespace-agnostik** parse edilir.
- Operasyonlar: `SaveProduct`, `GetProductByProductId`, `GetProductBySellerCode`,
  stok/fiyat güncelleme, sipariş listeleme/detay.
- **Referans verileri (host-global, tüm kiracılar paylaşır):**
  - `N11Category` — kategori taksonomisi + komisyon oranları; `N11MegaCategories`
    gruplaması (`N11CategoryMegaGrouper`)
  - `N11City` / `N11District` — adres taksonomisi. **Mahalleler saklanmaz** (talep anında).
  - `N11ShipmentCompany` — kargo firması aynası
- **Kargo şablonu (`N11ShipmentTemplate`)** — per-kanal (company-owned):
  `N11ShipmentMethod`, `N11DeliveryFeeType`, `N11ConditionalShippingUnit`,
  teslim edilebilir şehirler listesi.
- **Push doğrulayıcı (`N11ProductPushValidator`)**: gönderim öncesi zorunlu alan/format
  kontrolü — pazaryerine hatalı istek gitmeden yerelde patlar.
- **Web kazıma (`N11Scraper`)**: API'nin vermediği veriler için (dikkatli kullan).

## 12.4 Trendyol entegrasyonu
- **Protokol: REST/JSON.** Geçit: `https://apigw.trendyol.com` (**V2** — V1 kapanıyor)
- Kimlik: **Basic auth** `base64(apiKey:apiSecret)`
- **ZORUNLU header:** `User-Agent: "{sellerId} - SelfIntegration"` — eksikse **403**
- `sellerId` bazı uçlarda **path'e** girer
- **Rate limit (KRİTİK):** geçit agresif **429** döner — ~3 istek/sn'de bile.
  Salt-GET akışları **429'a dayanıklı** olmalı: `Retry-After` header'ı kadar (yoksa
  üstel geri çekilme, max 60 sn) bekle, **en fazla 6 deneme**. Her denemede **taze istek**
  kur. Tükenirse **dostane hata fırlat — sessiz kısmi sonuç YOK**.
  **POST/yazma bu yolu KULLANMAZ** (idempotent değil).
- Referans: `TrendyolCategory` (host-global taksonomi) + `TrendyolBrand`
  (**write-through cache** — tam senkron YOK, yalnız seçilen/ithal markalar).

## 12.5 Etsy entegrasyonu
- **Protokol: REST/JSON, Open API v3.** Kök: `https://api.etsy.com/v3`
- **Kimlik: OAuth 2.0 Authorization Code + PKCE (S256)**
  - Yetkilendirme: `https://www.etsy.com/oauth/connect`
  - Token: `https://api.etsy.com/v3/public/oauth/token`
  - Callback path: `/etsy/oauth-callback` (redirect URI = `App:SelfUrl` + path;
    Etsy uygulama kaydında **birebir**, büyük-küçük harf duyarlı, sondaki `/` olmadan)
  - Scope'lar: `listings_r listings_w shops_r shops_w transactions_r` — baştan istenir ki
    satıcıya **ikinci onay ekranı** çıkmasın
  - PKCE state/verifier geçici saklama: **10 dakika**
  - **Refresh token 90 gün; her yenilemede YENİ refresh token döner (rotasyon)** — sakla
  - Access token **süre payı 120 sn**: bitişe bu kadar kala süresi dolmuş say
- Referans: `EtsyTaxonomy` (host-global satıcı taksonomisi)
- Etsy'ye özel: `EtsyListingType`, `EtsyWhoMade`, mağaza bölümleri (shop sections),
  kargo profilleri (shipping profiles), iade politikaları (return policies)

## 12.6 Kanal ürünü (listeleme) modeli
Her pazaryeri için **aynı şekle sahip** bir aile:
```
SalesChannelTr{X}Product                       — ürün × kanal (company-owned)
 ├── SalesChannelTr{X}ProductAttribute         — kanal-özel varyant EKSENİ
 ├── SalesChannelTr{X}ProductAttributeValue    — kanal-özel varyant DEĞERİ
 ├── SalesChannelTr{X}ProductStockItem         — kanal-özel varyant override (fiyat/stok + marj)
 └── SalesChannelTr{X}ProductStockItemRecipeLine — kanal-özel varyant reçete satırları
```
**Desen: "klon-sonra-ayrış".** Çekirdek üründen klonlanır, sonra kanal bazında bağımsız
evrilir. Bu ailenin **N11, Trendyol, Etsy için üç ikizi** vardır.

**Devralma zinciri (`ChannelInheritance`)**: kanal-ürünü boş bıraktığı alanı üründen
devralır (menşe, para birimi, satıcı notu, kişiselleştirme, eklentiler). Efektif değer
çözümü **tek yerde** yaşamalıdır.

**Push ve import:**
- **Push**: yerel ürün → pazaryeri (doğrulama → dönüşüm → API çağrısı → sonuç/hata kaydı)
- **Import**: pazaryerinden mevcut ürünleri çekip yerel kayda bağlama
  (`TrendyolMarketplaceImportPanel`, `EtsyMarketplaceImportPanel`)
- **Görsel işleme**: `ImageUploadPipeline`, `MarketplaceImageDownloader`,
  `PublicImageLinkProvider` (pazaryerine gönderilecek görselin **herkese açık URL**'i;
  sağlayıcı yapılandırılabilir — ör. imgbb — ya da imzalı kendi bağlantımız)

## 12.7 Kargo (ÖNEMLİ MİMARİ KARAR)
> **KARGO YALNIZ SATIŞ KANALI SEVİYESİNDEDİR.**
> Çekirdekte ortak `ShipmentTemplate` / `Carrier` katmanı **YOKTUR ve KURULMAYACAKTIR**.
> **Gerekçe:** kargo şablonu ürünün değil **kanalın** özelliğidir (aynı ürün her
> pazaryerinde farklı şablonla gider) ve her pazaryerinin kendi kuralları olduğu için
> ortak çekirdek soyutlama **gereksizdir**. Ürün üzerinde `ShipmentTemplateId` **YOKTUR**.
>
> **Sistem otomatik cari AÇMAZ:** kargo firmasının carisi kullanıcı tarafından bağlanır.
> Hedeflenen model: kargo firmaları **kanal başına** tanımlanır; varsayılan cari alt hesap
> **ilk kullanımda kullanıcıya SORULUR**; aynı firma farklı kanallarda aynı cariyi
> gösterebildiği için borç tek bakiyede birikir.
>
> Reçetedeki kargo kalemi **ORTALAMA maliyettir** (tek paket varsayımı — fiyatlama için);
> **gerçek maliyet sipariş sürecinde kullanıcı tarafından girilir**.

**`MarketplaceShipmentTariff`** — pazaryerinin **yayımladığı** anlaşmalı kargo tarifesi
(desi fiyat tablosu). **Host-global, salt-okunur, yürürlük tarihli.** İzin aranmaz
(pazaryerinin herkese açık ilanıdır). `ShipmentChargeBasis` ücretlendirme tabanını belirler.
**`PackageDesiResolver`** desi çözüm zincirini (varyant → ürün → kanal) uygular.

---

# BÖLÜM 13 — SİPARİŞLER

## 13.1 Model — NÖTR sipariş
**Tüm satış kanallarının siparişleri TEK tabloda.** Kanal yalnızca ayrımcıdır
(`SalesChannelId` + `ChannelType`); kanal başına ayrı tablo/panel **YOKTUR**.

```
Order                    — company-owned, per-tenant
 ├── OrderLine            — sipariş kalemleri
 ├── OrderDetailSnapshot  — zengin detay (owned JSON; alıcı/adresler/tutar kırılımı/komisyon)
 ├── OrderOperationalData      — YEREL/operasyonel katman (O1)
 └── OrderLineOperationalData  — satır bazlı yerel katman
```

## 13.2 Faz O0 — SALT-OKUMA çekim (bugünkü kapsam)
- Pazaryerinden GET ile çekilir + **idempotent upsert** edilir.
- **FİŞ YOK · REZERVASYON YOK · STOK HAREKETİ YOK · pazaryerine YAZMA YOK.**
- Alanlar pazaryerinden gelen **snapshot**'lardır: yerel ürün/kanal silinse bile sipariş
  sağ kalır (VoucherLine felsefesi).
- **İdempotency anahtarı: `(SalesChannelId, RemoteOrderId)`** — ikinci çekim durumu/satırları
  **günceller**, dublike üretmez.

## 13.3 Alanlar
`RemoteOrderId` (değişmez), `OrderNumber`, `OrderDate` (**UTC** — zaman damgası),
`NeutralStatus`, `RemoteStatus` (ham kanal durumu, denetim için saklanır), `CustomerName`
(maskeli/kısa — tam kimlik saklanmaz), `TotalAmount` + `CurrencyUnitId`, `CargoProvider`,
`CargoTrackingNumber`, `FetchedAt` (çekim tazeliği), `Detail` (opsiyonel zengin snapshot).

## 13.4 Nötr durum (`OrderStatus`)
| Değer | Ad |
|---|---|
| 0 | `Unknown` — **çözülemeyen ham durum** (sessizce "New" varsayma; belirsizliği taşı) |
| 1 | `New` |
| 2 | `Processing` |
| 3 | `Shipped` |
| 4 | `Delivered` |
| 5 | `Cancelled` |
| 6 | `Returned` |

Ham kanal durumu → nötr eşleme **saf statik yardımcılarda**
(`N11OrderStatusMapper`, `TrendyolOrderStatusMapper`, `EtsyOrderStatusMapper`)
+ `N11OrderStatusCatalog` (ham durum kataloğu).

## 13.5 Yardımcılar
- `OrderLineProductMatcher` — sipariş kalemini yerel ürüne eşler
- `OrderLineProductSnapshotBuilder` — eşleşen ürünün snapshot'ını kurar
- `CargoTrackingUrlCatalog` — kargo firmasına göre takip URL'i üretir
- `OrderSyncManager` + `OrderSyncBackgroundWorker` — periyodik çekim (Trendyol'da
  **zaman penceresi** mantığı vardır; pencere testlerle korunmalı)

---

# BÖLÜM 14 — RAPORLAR

Tümü **çalışılan şirketle sınırlıdır**; istemcinin gönderdiği şirket **yok sayılır**
(sızıntı önleme). Şube filtresi verilirse şubenin **o şirkete ait olduğu doğrulanır**,
değilse **düşürülür** (cross-company forge koruması).

| Rapor | Kaynak | Davranış |
|---|---|---|
| **Pozisyon** | Bakiye defteri | `GROUP BY UnitId + SUM` (toplama **DB tarafında**), bilanço birimine re-base'li değerler, base-dışı net açık **DURUM** olarak toplanır. İşaret: `+` alacak/long, `−` borç/short. Base satırı **görünür ama durum dışı** (kendine karşı risk yok). Canlı — ~5 sn'de yenilenir. Kural hardcode değil: net tamamen defterden gelir = canlı poster davranışı. |
| **Bilanço** | `BalanceSheetSnapshot` | Tam net-varlık. Kapsam **Şube/Şirket** anahtarı + tarih → "Bilanço Al / Kaydet". `BalanceSheetScope` enum'u. |
| **İşlem** | Fiş satırları | **Cari-hesap bağımsız**, Company/Branch/Vault kapsamlı, tarih aralıklı işlem listesi |
| **Nakit** | Fiş/defter | Nakit hareketleri |
| **Maden** | Fiş/defter | Maden hareketleri |
| **Hurda** | Fiş/defter | Hurda hareketleri |
| **Mamül Stok / Mamül Hareket** | Fiş/defter | Good stok ve hareketleri |

> **Performans ilkesi:** Pozisyon raporu **rapor-zamanı yeniden hesaplama yapmaz** —
> poster çıktısı zaten kalıcıdır. Bu yüzden defter (ledger) vardır. Yeniden yazımda bu
> ayrımı koru; "raporda anlık hesaplayalım" **yanlış** yoldur.

---

# BÖLÜM 15 — DESTEK MODÜLLERİ

## 15.1 Medya kütüphanesi (DAM)
```
Media           — company-scoped, SELF-CONTAINED görsel VEYA video
MediaFolder     — klasör ağacı
EntityMediaLink — entity ↔ medya referansı (aynı medya çok yerde)
```
**İçerik DAİMA bizim blob'umuzdadır.** URL referansı **tutulmaz** — yükleme ya da
URL-import içeriği **indirip blob'a yazar**; kaynak silinse de bizde kalır.
**İçerik-hash ile dedup**: aynı içerik ikinci kez yazılmaz.
**Poster**: görselde küçültülmüş thumbnail, videoda çıkarılmış/kullanıcı-seçili kare.
Alanlar: `MediaType`, `BlobName`, `FileName`, `ContentType`, `Size`, `ContentHash`,
`PosterBlobName`.

**İmzalı genel bağlantı**: pazaryerine görsel gönderirken imzalı, süreli genel URL üretilir
(imzalama anahtarı + taban URL + ömür saat cinsinden yapılandırılır).

> **DONDURMA KURALI:** Yeni görsel/medya özelliği **YALNIZ DAM'a** yazılır.
> `ProductImage` / `MetalImage` (owned JSON tek/çok görsel) **DONMUŞ legacy**'dir —
> yeni özellik almaz (yalnız hata düzeltmesi). Yeniden yazımda **doğrudan DAM ile başla**;
> ikili modeli hiç kurma.

## 15.2 Ekler
`EntityDocument` (agnostik doküman eki) · `EntityNote` (agnostik not) — her ikisi de
`EntityName` + `EntityId` deseniyle **tek tablo tüm entity'lere** hizmet eder.

## 15.3 Coğrafya (host-global)
```
Country
 └── AdministrativeArea  (il/eyalet — ISO 3166-2 alt-bölüm; e-Fatura/UBL kimliği)
      └── Locality       (ilçe)
           └── SubLocality (mahalle/semt)
```
Tipler: `AdministrativeAreaType`, `LocalityType`, `SubLocalityType`, `PostalCodeType`.
Adres değer nesnesi: `Address` + **UBL uyumlu** `UblPostalAddress` (Framework modülünde).

## 15.4 Takvim
`SchedulerAppointment` — company-scoped randevu; takvim ekranı.

## 15.5 Kullanıcı arayüz durumu
`UserGridLayout` — **kullanıcı × grid** başına kolon düzeni (genişlik/sıra/sıralama),
serileştirilmiş JSON. **Ayrı tablo** olmalıdır; "tüm gridler tek ayar sözlüğü" yaklaşımı
ayar değerini taşırıp keser.
`TradeXpressUiSettingNames` — diğer UI tercihleri (MDI sekme davranışı vb.).

---

# BÖLÜM 16 — KULLANICI ARAYÜZÜ SPESİFİKASYONU

## 16.1 Kabuk: MDI sekmeli masaüstü hissi
- Sol tarafta **ağaç menü** (filtre kutulu). Menü düğümüne tıklamak sayfayı **MDI sekmesi**
  olarak açar veya zaten açıksa **aktive eder**.
  *(Uygulama notu: "seçim değişti" olayı değil, "düğüme tıklandı" olayı kullanılmalı —
  aksi halde zaten seçili düğüme tekrar tıklayınca sekme aktive edilemez.)*
- Çözümlenemeyen URL'ler (yönetim sayfaları) tam navigasyona düşer (MDI dışı).
- Sekme **içerik durumu korunur** (arama metni, filtreler, seçim) — sekmeler arası geçişte kaybolmaz.
- Üst barda: **çalışılan şirket/şube seçici**, kullanıcı menüsü, oturum tipi rozeti,
  ayarlar paneli.

## 16.2 Menü ağacı (kök sıra — günlük kullanım üstte)
```
1. Cari İşlemler          /cari-islemler          (kimlik doğrulanmış)
2. Transferler            /transfers              (Confirmations.Propose)
3. Teyitler               /confirmations          (Confirmations.View)
4. Siparişler             /orders                 (SalesChannels.Default)
5. Takvim                 /scheduler              (Appointments.Default)
6. Tanımlar
   ├── Finansal
   │    ├── Para Birimleri        /currencies/currency-units
   │    └── Pariteler             /currencies/parities
   ├── Satış                                        ← satışa odaklı HER ŞEY burada
   │    ├── Satış Kanalları       /sales-channels
   │    ├── Ürünler               /products
   │    ├── Ürün Kategorileri     /product-categories
   │    ├── Varyant Tanımları     /variant-templates
   │    ├── Eklentiler            /add-ons
   │    ├── Reçete Şablonları     /recipe-templates
   │    ├── Muadil Grupları       /substitutions
   │    └── Kargo Tarifeleri      /marketplace-shipment-tariffs   (host-global, izinsiz)
   ├── Emtialar                                     ← SAF emtia kataloğu = reçetenin GİRDİLERİ
   │    ├── Nakitler /cashes · Hizmetler /services · Vadeliler /futures
   │    ├── Hurdalar /scraps · Madenler /metals · Taşlar /stones
   │    └── Mücevherler /jewelries · Mamüller /goods
   ├── Organizasyonlar
   │    ├── Şirketler             /companies
   │    ├── Cari Hesaplar         /accounts
   │    └── Ayar Evleri           /assay-offices    ← DIŞ KURUM, emtia değil
   ├── Ülkeler                    /countries
   └── Medya Kütüphanesi          /media
7. Raporlar
   ├── Pozisyon /reports/position · Bilanço /reports/balance-sheet
   ├── İşlem /reports/transactions · Nakit /reports/cash
   └── Maden /reports/metal · Hurda /reports/scrap · Mamül stok/hareket
8. Yönetim  (kullanıcılar, roller, izinler, kiracılar, ayarlar)
```
**Kapsam kuralı:** Şirket/Cari/Ürün/Kanal gibi company-owned kayıtlar **yalnız kiracı
oturumunda** görünür (host'ta tanımlanamaz). Host-global kataloglar (kargo tarifesi,
ülkeler) **her iki oturumda** görünür. Grubu topluca kiracıya kapatmak host'tan katalogu
gizlerdi — **koşul kalem bazındadır, grup bazında değil**.

**Şube/Kasa ve Alt Hesap ayrı menü DEĞİLDİR** — parent düzenleme formundaki drill
listeleriyle yönetilir.

## 16.3 CRUD çatısı (yeniden kullanılabilir, merkezî)
Tek bir jenerik CRUD düzeni tüm liste sayfalarına hizmet eder:
- **Sunucu-taraflı** sayfalama / sıralama / filtreleme / arama
- Kolon filtre satırı — **sunucu tarafı filtreyi işlemeyen sayfalarda KAPATILMALIDIR**
  (aksi halde kullanıcı filtreler, sonuç değişmez → yanıltıcı)
- Arama modu: **Sunucu taraflı** (tüm kayıtlarda, varsayılan) veya **grid-içi** (yüklü veride)
- **Grid kolon düzeni kullanıcı başına kalıcı**
- Bağlam menüsü, dışa aktarma (Excel), kayıt gezinme (önceki/sonraki)
- Toolbar aksiyonları **tek kaynaktan** üretilir; sabit sıra numaraları:
  `Yeni=0 · Kaydet=10 · Kaydet ve Yeni=20 · Sil=100 · [özel=300] · Ara=400 · Dışa Aktar=500 ·
  Yenile=600 · Önceki=700 · Sonraki=710 · Geri Al=800 · Yinele=810 · Sıfırla=820 ·
  Aktif Filtresi (sağda)=1000`
- **Drill list**: parent düzenleme formunun içinde child listesi (Şirket→Şube, Şube→Kasa,
  Hesap→Alt Hesap, Ürün→Varyant, Kanal→Kanal Ürünü)
- Düzenleme formları **doğrudan Get-DTO** üzerinde çalışır (ayrı client ViewModel katmanı
  **YOKTUR**); kaydetmede nesne eşleyici ile giriş DTO'suna dönüştürülür
- Onaylı silme diyaloğu, doğrulama, hata sunumu merkezîdir
- Toplu (batch) işlem desteği
- Otomatik toparlanan hata sınırı (bir bileşen patlarsa sayfa ölmez) + geliştirici hata paneli

## 16.4 Cari İşlemler ekranı (uygulamanın en yoğun kullanılan formu)
**Adaptif üç panelli yerleşim:**
```
P1: Cari/Kasa seçim paneli  — seçilince KİLİTLENİR (yanlışlıkla değişmesin)
P2: İşlem listesi + ekler   — sekmeli; seçili carinin fiş satırları grid'i
P3: İşlem giriş paneli      — ProcessType'a göre DEĞİŞEN panel
```
- Liste modunda P1 **gizlenir ama durumu korunur** (Geri'de cari/fiş seçimi kaybolmaz).
- Mobilde tek sütuna iner.
- Düzelt/Sil satır-içi ikon değil **toolbar başında**; seçili satır(lar) üzerinde çalışır.
- İşlem tipi başına ayrı giriş paneli:
  `MetalProcessPanel` · `ScrapProcessPanel` · `CashProcessPanel` · `ConvertProcessPanel` ·
  `ServiceProcessPanel` · `FutureProcessPanel` · `BullionProcessPanel` · `BullionExitPanel` ·
  `AssayProcessPanel` · `DebitNoteProcessPanel` · `TransferProcessPanel` +
  ortak taban (`ProcessPanelBase`, `CommodityProcessPanelBase`)
- Satır bazlı ek/doküman diyaloğu.
- **Transferler ekranı aynı formun organizasyon-içi kipidir** (§9).

## 16.5 Diğer önemli ekranlar
- **Ürün düzenleme**: kimlik + görseller + nitelikler/varyantlar + **reçete paneli** +
  **satış kanalları paneli** (kanal başına listeleme durumu) + kategori seçici
- **Kanal ürünü düzenleme**: kanal-özel alanlar + kategori seçici (N11/Trendyol/Etsy ayrı) +
  nitelik ızgarası + push/import butonları
- **Sipariş listesi**: tek grid, kanal/durum/tarih filtresi, kalem drill'i, takip no gösterimi
- **Teyit kutusu**: gelen/giden, durum bazlı
- **Medya kütüphanesi**: klasör ağacı + ızgara + yükleme
- **İzin yönetimi**: izin ağacı editörü + **kapsamlı rol editörü** (şirket/şube/kasa)

## 16.6 UI konvansiyonları (mekanik olarak zorlanır)
- Yeni bileşende **kod-arkası ayrı dosyada** (işaretleme dosyasında gömülü kod bloğu **yasak**)
- **Ad-hoc sembol/emoji ikon YASAK** — merkezî ikon kataloğu kullanılır
- Bileşen içinde **tam-nitelikli tip enjeksiyonu YASAK** — merkezî import dosyası
- Yeni CSS dosyası/sınıfı **onay gerektirir**; satır-içi stili "temizlik" diye CSS'e taşıma
- Dil: **TR + EN tam parite** (~2.000 anahtar). Eksik anahtar **testte kırmızı** olur.

---

# BÖLÜM 17 — ARKA PLAN İŞLERİ

| İş | Görevi | Not |
|---|---|---|
| `HaremPlaywrightFeedWorker` | Canlı kur beslemesi (headless tarayıcı kazıma) | ~5 sn çekim, ~15 dk kalıcılaştırma, ~2 dk tazelik eşiği |
| `N11CategorySyncWorker` | N11 kategori taksonomisi senkronu | host-global |
| `N11ReferenceSyncWorker` | N11 şehir/ilçe/kargo firması senkronu | host-global |
| `EtsyTaxonomySyncWorker` | Etsy satıcı taksonomisi senkronu | host-global |
| `OrderSyncBackgroundWorker` | Pazaryerlerinden sipariş çekimi | idempotent upsert |
| `RepricingCycleWorker` | Periyodik yeniden fiyatlama | olay yayar |
| `ProductStockSyncJob` | Kanal stok güncelleme | orkestratör tetikler |

**Kimlik kapsamı:** arka plan işleri kullanıcı oturumu olmadan çalışır; her iş **kiracı ve
şirket bağlamını açıkça kurmalıdır** (`OrchestrationIdentityScope` deseni). Aksi halde
sorgu filtreleri yanlış çalışır.

**Olaylar (event):** `MetalStockChangedEto`, `RepricingCycleElapsedEto` — gevşek bağlı
orkestrasyon için.

---

# BÖLÜM 18 — BAŞLANGIÇ VERİSİ (SEED)

Kurulumda tohumlanan veriler. **Sıra önemlidir.**

1. **Host seviyesi:**
   - `CountrySeeder` — ülkeler (ISO 3166)
   - `GeographySeeder` — idari alanlar / yerellikler
   - `CurrencyUnitSeeder` — TRY, USD, EUR, HAS, GUM, PLT, PLD…
   - `ParitySeeder` — kanonik parite çiftleri
   - `MarketplaceShipmentTariffSeeder` — pazaryeri kargo tarifeleri
   - `N11MegaCategorySeeder` — N11 mega kategori grupları
2. **Kiracı seviyesi:**
   - `OrgSeeder` — HQ şirket → HQ şube → varsayılan kasa
   - `ScopedGrantSeeder` — rolsüz kullanıcıya "saf coğrafi erişim" grant'ı (geri uyum)
3. **Şirket seviyesi (organizasyon tohumundan SONRA):**
   - `MetalSeeder` · `ScrapSeeder` · `FutureSeeder` · `ServiceSeeder` · `CashSeeder` —
     **tenant'ın HER şirketine ayrı set** kurulur

**Backfill (geçiş) yardımcıları** — mevcut veriyi yeni şemaya taşıyan, **idempotent**
(dolu satıra dokunmayan) bileşenler: `CompanyOwnedBackfiller`, `CountryReferenceBackfiller`,
`BalanceLedgerBackfiller`, `RecipeLineOriginBackfiller`. Yeniden yazımda bunlara ihtiyacın
olmayabilir — ama **defter yeniden inşa edici** (`BalanceLedgerBackfiller` muadili) mutlaka
olsun: defter bozulursa fişlerden yeniden üretilebilmelidir.

---

# BÖLÜM 19 — KONVANSİYON AĞLARI (MEKANİK ZORLAMA)

Bu sistem kurallarını **derleme ve test zamanında mekanik olarak** zorlar. Kırmızıysa kural
çiğnenmiştir ve sessizce geçilemez. İstisna = açık izin listesi + gerekçe;
**asla "testi gevşetme" ya da uyarı bastırma**.

## Derleme zamanı (yalnız Domain + Domain.Shared, hata seviyesinde)
Yasaklı API listesi:
- **`Guid.NewGuid()`** — kimlik üretimi enjekte edilen bir üreticiden gelmeli (test edilebilirlik)
- **Ham .NET istisna constructor'ları** — tipli iş istisnası kullan
- Ham null/boşluk kontrolü yardımcıları — merkezî `StringFieldGuard` kullan
- **Expression-bodied member**: kökte uyarı, Domain'de **hata** (otomatik özellik ve lambda muaf)

## Test zamanı (konvansiyon testleri)
| Test | Neyi korur |
|---|---|
| `EntityConventionTests` | Constructor'da `Id`/`TenantId` yok · `SetActive(bool)` var · `ToString` override · setter'lar korumalı |
| `AppServiceConventionTests` | **Elle yazılmış statik entity→DTO eşleyici YASAK** (eşleyici üretilmeli) |
| `NavigationConventionTests` | Aggregate'ler arası **id-only** referans (navigation yok) |
| `RazorConventionTests` | Yeni bileşende gömülü kod bloğu yasak · ad-hoc emoji/sembol ikon yasak · tam-nitelikli enjeksiyon yasak |
| `RazorComponentParameterTests` | Bileşen parametreleri tanımlı ve tutarlı |
| `LocalizationParityTests` | **TR/EN anahtar paritesi** |
| `PagingConventionTests` | Sayfalama sözleşmesi |
| `CompanyScopedFilterTests` | **Sahipsiz emtia inşa edilemez** (7 emtia ailesi için ayrı ayrı) |
| `OrgCodeUniquenessTests` | Organizasyon kodu benzersizliği |
| bUnit bileşen testleri | Bileşenler **gerçekten render olur** — tanımsız parametre / eksik bağımlılık derlemede değil çalışma anında patlar; bu testler onları yakalar |

**Kural:** yeni bir mimari kural koyduğunda buraya bir doğrulama ekle — doğru kullanım
GEÇSİN, ihlal KIRMIZI olsun.

---

# BÖLÜM 20 — İNŞA SIRASI (FAZLAR)

Her fazın sonunda **çalışan, test edilmiş** bir dilim teslim et.

**Faz 0 — İskelet**
Katmanlar, DI, kimlik doğrulama/yetkilendirme, çok-kiracılık, sorgu filtreleri, temel CRUD
çatısı, lokalizasyon altyapısı, konvansiyon testleri (§19).

**Faz 1 — Finansal çekirdek**
CurrencyUnit + marj + follow · Parity · ExchangeRate + canlı besleme + fiyat hesaplayıcı.
*Kabul:* USD/TRY paritesi doğru yönde okunuyor; marj tipleri dördü de doğru hesaplıyor.

**Faz 2 — Organizasyon ve yetki**
Company/Branch/Vault + ağaç değişmezleri · Account/SubAccount · izin ağacı ·
`UserScopedGrant` + kapsam çözümleme · **çalışma bağlamı** (§2.3).
*Kabul:* B şirketinin kullanıcısı A şirketinin cari hesabını **hiçbir yoldan** göremiyor.

**Faz 3 — Emtia katalogları + varyant sistemi**
Yedi emtia ailesi (company-owned) · agnostik nitelik/değer/varyant · varyant şablonları ·
özel kodlar · ekler (doküman/not) · medya (DAM).

**Faz 4 — İşlem motoru (EN KRİTİK)**
Voucher + VoucherLine · tüm enum'lar · saf hesap motoru · **13 poster** · bakiye defteri +
senkronizasyon · satır geçmişi · Cari İşlemler ekranı.
*Kabul:* Her poster için ödeme-tipi × yön kombinasyon testleri yeşil; fiş silinince defter
satırları da gidiyor; fiş güncellenince defter yeniden yazılıyor.

**Faz 5 — Takoz, çeşni, virman, teyit**
Çok-metalli takoz mekaniği · dağıtım modları · çeşni · virman çift bacağı (`LinkId`) ·
teyit akışı (Propose→Declare→Confirm/Reject) + ayna anahtarı.

**Faz 6 — Raporlar**
Pozisyon (canlı) · Bilanço (snapshot) · İşlem · Nakit/Maden/Hurda/Mamül.

**Faz 7 — Ürün, reçete, fiyatlama**
Product · reçete satırları + türev mekaniği · canlı maliyet hesabı · fiyat türetme ·
reçete şablonları · ürün kategorileri + nitelik mirası.

**Faz 8 — Muadil motoru + orkestrasyon**
Muadil grubu/çözücü/planlayıcı/materyalize edici · maden stok okuyucu · satılabilir stok
hesaplayıcı · ters indeks · aşırı satış koruması · yeniden fiyatlama döngüsü.

**Faz 9 — Pazaryerleri**
Kanal modeli (TPT) · sağlama · N11 (SOAP) · Trendyol (REST + 429 dayanıklılığı) ·
Etsy (OAuth PKCE) · kanal ürünü aileleri · push/import · kategori eşleştirme köprüsü ·
yan maliyetler + gross-up.

**Faz 10 — Siparişler**
Nötr sipariş modeli · idempotent çekim · durum eşleyiciler · ortak sipariş paneli ·
operasyonel katman.

---

# BÖLÜM 21 — TUZAKLAR VE YASAKLAR (GEÇMİŞTE ÖĞRENİLENLER)

Bunlar bu projede **gerçekten yaşanmış** hatalardır. Tekrarlama.

## Mimari tuzaklar
1. **Emtiaları host seviyesinde/sahipsiz yapma.** "Holding katmanı" cross-company
   manipülasyonun taşıyıcısıdır. Emtia **company-owned**, `CompanyId` **non-nullable**.
2. **`CurrentCompanyId == null`'ı "hepsi görünür" anlamına getirme.** Yetkisiz seçimde
   `null`'a düşmek **ters güvenliktir** — ilk izinli şirkete düş ya da sentinel kullan.
3. **Kargo için ortak çekirdek katman kurma.** Kargo kanalın özelliğidir; ortak soyutlama
   silindi çünkü gereksizdi (§12.7).
4. **Ürüne kargo şablonu bağlama.** Aynı ürün her pazaryerinde farklı şablonla gider.
5. **Sistem otomatik cari hesap açmasın.** Şirket kendi cari planını kendisi yönetir.
6. **Raporda anlık hesaplama yapma.** Poster çıktısı kalıcı deftere yazılır; rapor okur.
7. **Kanal-ürün entity'sinde "Listing" kelimesini kullanma** (§11.3).
8. **İki paralel görsel modeli kurma** — doğrudan DAM ile başla (§15.1).
9. **Client-side ViewModel katmanı kurma** — düz formlar doğrudan Get-DTO üzerinde çalışır.

## Veri/hesap tuzakları
10. **`"1"` ile `"01"` aynı değildir.** Kod eşleştirmede sıfır dolgusu anlamlıdır;
    sayıya çevirip karşılaştırma.
11. **İş tarihlerini UTC'ye çevirme.** Gün kayar, ay sonu fişi bir önceki aya düşer.
12. **Ara hesapları yuvarlama.** Yalnız kalıcılaşan değer N2'ye yuvarlanır.
13. **Kur snapshot'larını sonradan okuma.** Takozda kurlar kayıt anında dondurulur;
    poster ek okuma yapmaz.
14. **Emtia referansına FK koyma.** Snapshot'tır; katalog silinse fiş okunabilir kalmalı.
15. **Polimorfik karşı-taraf kolonuna FK kurmaya çalışma** (tek kolon iki tabloyu gösterir).
16. **Gross-up oranlarının toplamını denetlemeden geç.** Payda sıfırlanır/negatifleşir.
17. **İkinci bir aktif `AutoRate` kalemine izin verme** — aynı oran iki kez sayılır.

## Süreç tuzakları
18. **Kod yazmadan önce dört soruyu sor:**
    `[varsaydım mı / doğruladım mı?]` · `[bu bileşen/servis zaten var mı — aradım mı?]` ·
    `[hangi test kırılabilir?]` · `[geri alınabilir mi?]`
    İkinci soruyu **her katman için ayrı** sor (UI'da sorup domain servislerinde sormamak,
    aynı çözücünün üçüncü kez yazılmasına yol açtı).
19. **Tıkanınca çalışan implementasyonu silme.** Kök nedeni bul, küçük geri-alınabilir
    seçenek üret, onay al.
20. **Aynı düzeni 2+ dosyaya uygulamadan önce tek örnek göster, onay al.** Topyekûn
    tarama-değiştirme yok.
21. **Mock dönmek, `catch {}` ile hata gizlemek, uyarı bastırmak, test zayıflatmak yasaktır.**
22. **Paket sürümünü hatadan kaçmak için oynatma.**
23. **Merkezî yolu (ortak CRUD düzeni, durum servisi, framework modülü) bypass edip
    paralel tek-kullanımlık yapı kurma.**

---

# BÖLÜM 22 — MİMARİ İLKELER (SÜREKLİ UYGULA)

- **DRY · SOLID · KISS · YAGNI · Kalıtım yerine kompozisyon · Fail-fast · Tek doğruluk
  kaynağı (SSOT) · Söyle-Sorma (Tell-Don't-Ask) · Kapsülleme · En Az Şaşırtma**
- **En üst bilinç:** kod ne kadar merkezî ve yeniden kullanılabilir olursa başka
  UI'lara/projelere devredilmesi o kadar kolay olur → **en merkezî, override ile genişleyen
  yerleşimi seç**. Jenerik/yeniden kullanılabilir kod **Framework katmanına**, uygulamaya
  özel kod uygulama katmanına.
- **Klasör = ad alanı (namespace).** Ad alanı klasör yolunu birebir izler; **tüm
  katmanlarda ve testlerde aynı** (ör. `Financials/{CurrencyUnits, Parities, ExchangeRates}`).
  Yeni dosya doğru kovaya; kök/gevşek dizine bırakma.
- **Hiçbir tipi satır içinde tam-nitelikli yazma.** Ad alanı öneki koku sayılır; ortak
  import dosyalarında topla, kodda kısa ad kullan.
- **Dokunduğun kapsamda ihlal görürsen sadece raporlama — düzelt.** Ama artımlı yap;
  ayrı "refactor" görevi açma.

---

# BÖLÜM 23 — KABUL KRİTERLERİ

Yeniden yazım **şu koşullar sağlandığında** tamamlanmış sayılır:

1. **Güvenlik:** Farklı şirketlerin kullanıcıları birbirinin sahipli verisini hiçbir uçtan
   (liste, detay, doğrudan silme, rapor) göremez/değiştiremez. Test ile kanıtlanmış.
2. **Muhasebe doğruluğu:** 13 işlem türünün her biri için, her ödeme tipi × yön
   kombinasyonunda bakiye etkisi beklenen değerdedir. Fiş güncelleme/silme defteri
   tutarlı bırakır.
3. **Zaman:** İş tarihleri gün kaydırmaz; zaman damgaları kullanıcı yerel saatinde görünür.
4. **Para:** Tüm parasal alanlar decimal; yuvarlama yalnız kalıcılaşmada; kanonik ölçekler korunur.
5. **Organizasyon değişmezleri:** Her şirketin ≥1 şubesi, her şubenin ≥1 kasası var;
   tenant başına tek HQ; HQ devir-önce-sil çalışıyor.
6. **Teyit:** Tek taraflı beyan karşı defteri kımıldatmıyor; yalnız çift-teyitli ayna
   atomik postlanıyor; ayna tutmazsa fark raporlanıyor.
7. **Muadil + orkestrasyon:** Maden stoğu değişince satılabilir adet yeniden hesaplanıyor;
   aşırı satış senaryosu testte engelleniyor.
8. **Pazaryerleri:** Üç kanalda da kimlik doğrulama, ürün gönderimi ve sipariş çekimi
   uçtan uca çalışıyor; Trendyol 429'da veri kaybetmeden yeniden deniyor; Etsy refresh
   token rotasyonu kalıcılaşıyor; sipariş çekimi idempotent.
9. **Raporlar:** Pozisyon raporu defterden okuyor (yeniden hesaplamıyor) ve çalışılan
   şirketle sınırlı.
10. **UI:** MDI sekmeleri durum koruyor; grid kolon düzeni kullanıcı başına kalıcı;
    TR/EN tam parite.
11. **Konvansiyon ağları kurulu ve yeşil** (§19).

---

# BÖLÜM 24 — AJANA SON NOT

Bu spesifikasyon bir **davranış sözleşmesidir**, kod dökümü değil. Seçtiğin platformda
daha iyi bir kalıp varsa **kullan** — ama sözleşmedeki iş kuralını, işaret konvansiyonunu,
güvenlik sınırını ve değişmezi **değiştirme**.

En kritik üç yer, sırayla:
1. **Bakiye defteri + poster mimarisi** (§8.8) — sistemin parası burada.
2. **Çalışma bağlamı + sahiplik filtresi** (§2.3–2.4) — sistemin güvenliği burada.
3. **Reçete/fiyatlama + muadil orkestrasyonu** (§10) — sistemin ticari değeri burada.

Emin olamadığın her kritik kararda **kod yazmadan dur ve sor**.
