using System;
using System.Collections.Generic;
using System.Linq;
using Integration.TradeXpress.MultiCompany;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.TrendyolProducts;

/// <summary>Trendyol KATEGORİ attribute değeri (id-bazlı; Trendyol attributeId + attributeValueId ya da serbest
/// customValue) — owned, JSON kolonuna serialize edilir. Ad "CategoryAttribute" (N11 sözlüğüyle hizalı, S6 rename):
/// varyant-kombinasyon üreten <see cref="SalesChannelTrTrendyolProductAttribute"/> ENTITY'sinden tamamen ayrıdır.</summary>
public class SalesChannelTrTrendyolProductCategoryAttribute
{
    /// <summary>Trendyol attribute id'si (kategori attribute tanımından).</summary>
    public int AttributeId { get; set; }

    /// <summary>Trendyol attribute value id'si (değer listesinden seçilen). Serbest değerde null.</summary>
    public int? AttributeValueId { get; set; }

    /// <summary>Serbest (custom) değer — attribute değer listesi kabul etmiyorsa. Value id ile birlikte kullanılmaz.</summary>
    public string? CustomValue { get; set; }

    public SalesChannelTrTrendyolProductCategoryAttribute()
    {
    }

    public SalesChannelTrTrendyolProductCategoryAttribute(int attributeId, int? attributeValueId, string? customValue)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
        CustomValue = customValue;
    }
}

/// <summary>Varyant-belirleyici (varianter) attribute'un id çifti — SKU yeniden-bağlama imzasının temeli.
/// <see cref="SalesChannelTrTrendyolProductSku.AttributeSnapshot"/> içinde tutulur (name/value DEĞİL, id/valueId:
/// Trendyol id-bazlı olduğu için kültür/tr-TR normalizasyonuna gerek kalmaz).</summary>
public class SalesChannelTrTrendyolProductSkuAttribute
{
    public int AttributeId { get; set; }
    public int AttributeValueId { get; set; }

    public SalesChannelTrTrendyolProductSkuAttribute()
    {
    }

    public SalesChannelTrTrendyolProductSkuAttribute(int attributeId, int attributeValueId)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
    }
}

/// <summary>Pazaryerinin kalem için BİLDİRDİĞİ varianter (eksen) değeri — içe aktarım anının snapshot'ı
/// (<c>TrendyolVariantAxisResolver</c> çıkarımı; "50 ml" / "Kırmızı" gibi kalemler arasında DEĞİŞEN nitelik).
/// <see cref="SalesChannelTrTrendyolProductSkuAttribute"/> (yeniden-BAĞLAMA imzası; push planında yazılır) ile
/// KARIŞTIRILMAZ — o imzadır, bu pazaryeri beyanıdır; ikisini tek listede tutmak imza eşleştirmesini import
/// verisiyle kirletirdi. Push body'si bu değerleri item-düzeyi attribute olarak GERİ gönderir: değerler
/// Trendyol'un kendi beyanı olduğundan kategori-tanımı doğrulaması gerektirmez. Kimliksiz (serbest metin)
/// eksen değeri <see cref="ValueText"/> ile taşınır (<see cref="AttributeValueId"/> null).</summary>
public class SalesChannelTrTrendyolProductSkuRemoteAxisValue
{
    public int AttributeId { get; set; }
    public int? AttributeValueId { get; set; }

    /// <summary>Değerin OKUNUR metni ("Kırmızı"/"50 ml") — id'li değerde de saklanır (PushHistory ve etiketler
    /// için); body'ye custom olarak yalnız id yokken gider.</summary>
    public string? ValueText { get; set; }

    /// <summary>Niteliğin OKUNUR adı ("Renk") — pazaryerinin bildirdiği; PushHistory "Ad=Değer" biçimi buradan kurulur.</summary>
    public string? AttributeName { get; set; }

    public SalesChannelTrTrendyolProductSkuRemoteAxisValue()
    {
    }

    public SalesChannelTrTrendyolProductSkuRemoteAxisValue(
        int attributeId, int? attributeValueId, string? valueText, string? attributeName)
    {
        AttributeId = attributeId;
        AttributeValueId = attributeValueId;
        ValueText = valueText;
        AttributeName = attributeName;
    }
}

/// <summary>Trendyol SKU kimlik satırı (varyant-başına; owned → JSON). <see cref="Barcode"/> İLK başarılı push'ta
/// üretilir ve DONDURULUR: ProductVariant.Code sonradan değişse ya da synchronizer varyantı silip yeniden üretse bile
/// push aynı uzak Trendyol item'ına gider (satıcı-geneli barcode; onaylı üründe DEĞİŞTİRİLEMEZ). <see cref="StockCode"/>
/// = merchantSku (variant-bulk ile güncellenebilir; mutable). <see cref="RemoteContentId"/> = productContentId
/// (content-bulk-update kimliği; başarılı push sonrası dolar). <see cref="AttributeSnapshot"/> = varianter attribute
/// id çiftleri (yeniden-bağlama imzası).</summary>
public class SalesChannelTrTrendyolProductSku
{
    /// <summary>Bağlı ERP varyantı (yeniden üretilirse kod/imza üzerinden bu alana yeniden bağlanır).</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Trendyol satıcı-geneli barcode — DONDURULMUŞ ("{VaryantKodu}-{SequenceNo}", kuruluş anındaki kod).</summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>Trendyol stok kodu (= merchantSku; variant-bulk ile güncellenebilir).</summary>
    public string StockCode { get; set; } = string.Empty;

    /// <summary>Trendyol'un atadığı içerik id'si (productContentId; content-bulk-update kimliği). Başarılı push'ta dolar.</summary>
    public long? RemoteContentId { get; set; }

    /// <summary>Push edilen varianter attribute id çiftleri — yeniden-bağlama imzası.</summary>
    public List<SalesChannelTrTrendyolProductSkuAttribute> AttributeSnapshot { get; set; } = new();

    /// <summary>Pazaryerinin kalem için bildirdiği EKSEN değerleri (import görüntüsü) — push body'sinde
    /// item-düzeyi attribute kaynağı. <see cref="AttributeSnapshot"/>'tan AYRI: o bizim gönderdiğimiz imza,
    /// bu pazaryerinin beyanı. Boş liste = "eksen yok" beyanı (tek kalemli grup).</summary>
    public List<SalesChannelTrTrendyolProductSkuRemoteAxisValue> RemoteVariantAttributes { get; set; } = new();

    // ── PAZARYERİNİN KENDİ BEYANI (import görüntüsü) — bizim gönderdiğimizden AYRI ────────────────────
    // 2026-08-10: import bu üç değeri Trendyol'dan ZATEN alıyordu (TrendyolRemoteVariant.Quantity/ListPrice/
    // SalePrice) ama hiçbir yere yazmıyordu. Sonuç: kanal-ürün listesinde fiyat/stok kolonları BOŞTU ve tek
    // kaynak LastSent* olduğu için hiç push edilmemiş 224 üründe kalıcı olarak boş kalacaktı — oysa cevap
    // elimizdeydi, atılıyordu. Bu alanlar push zincirini ETKİLEMEZ (fiyat StockItem override'larından yürür).

    /// <summary>Import anında pazaryerinde görünen ADET. null = hiç import edilmedi.</summary>
    public int? RemoteQuantity { get; set; }

    /// <summary>Import anında pazaryerindeki liste fiyatı (indirim öncesi referans).</summary>
    public decimal? RemoteListPrice { get; set; }

    /// <summary>Import anında pazaryerindeki SATIŞ fiyatı (müşterinin ödediği).</summary>
    public decimal? RemoteSalePrice { get; set; }

    // ── PAZARYERİNİN ENGEL BEYANI (import görüntüsü) ──────────────────────────────────────────────────
    // 2026-08-13: bu bayraklar Trendyol yanıtında HEP vardı ve hiç okunmuyordu. Bedeli sessizdi: karalisteye
    // alınmış ya da kilitlenmiş bir kalem bizde "onaylı + satışta" görünüyor, gönderim karşı tarafta
    // reddediliyor, sebebi ancak hata metninden anlaşılıyordu. Canlı ölçüm teorik olmadığını gösterdi —
    // 19 kalemlik grubun TAMAMI blacklisted, dördü ayrıca locked.
    //
    // Üç durumlu okunur: null = "pazaryeri bildirmedi", false = "engel yok" BEYANI. İkisini birleştirmek,
    // bildirilmemiş bir engeli "engel yok" diye kaydetmek olurdu.
    //
    // Bu alanlar push zincirini DURDURMAZ (bkz. TrendyolListingObstacleResolver özeti) — görünür kılar.

    /// <summary>Kalem pazaryerinde ARŞİVLENMİŞ mi.</summary>
    public bool? RemoteArchived { get; set; }

    /// <summary>Listeleme KİLİTLİ mi (gönderim kabul edilmez).</summary>
    public bool? RemoteLocked { get; set; }

    /// <summary>Kilit gerekçesi — pazaryerinin KENDİ metni (ör. "UNSUPPLIED_PRODUCT"); yeniden yazılmaz.</summary>
    public string? RemoteLockReason { get; set; }

    /// <summary>Kalem KARALİSTEDE mi (satışa çıkamaz).</summary>
    public bool? RemoteBlacklisted { get; set; }

    /// <summary>Karaliste gerekçesi — pazaryerinin kendi metni.</summary>
    public string? RemoteBlacklistReason { get; set; }

    /// <summary>Kalem REDDEDİLMİŞ mi.</summary>
    public bool? RemoteRejected { get; set; }

    /// <summary>Red gerekçeleri (birden çoksa " · " ile birleşik).</summary>
    public string? RemoteRejectReason { get; set; }

    /// <summary>Pazaryerinde AKTİF KAMPANYA var mı. Otonom fiyat güncellemesi bu kaleme yazmadan önce
    /// kullanıcının görmesi gereken tek şey budur — kampanyalı fiyata müdahalenin sonucu bizde modellenmedi.</summary>
    public bool? RemoteHasActiveCampaign { get; set; }

    /// <summary>Kalemin Trendyol'daki sayfası — listede satırdan doğrudan gidilir.</summary>
    public string? RemoteProductUrl { get; set; }

    /// <summary>Kalemin pazaryerinde oluşturulma anı (UTC).</summary>
    public DateTime? RemoteCreatedAtUtc { get; set; }

    /// <summary>Kalemin pazaryerinde son güncellenme anı (UTC) — "bizim dışımızda değişti mi" sorusunun
    /// tek kanıtı. Kendi <c>LastSyncedAt</c>'imizden sonraysa kanalda bize ait olmayan bir müdahale olmuştur.</summary>
    public DateTime? RemoteUpdatedAtUtc { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen adet (dirty-tracking temeli).</summary>
    public int? LastSentQuantity { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen listePrice (indirim öncesi referans).</summary>
    public decimal? LastSentListPrice { get; set; }

    /// <summary>Son BAŞARILI push'ta gönderilen salePrice (efektif satış fiyatı).</summary>
    public decimal? LastSentSalePrice { get; set; }

    // ── GÖNDERİLDİ AMA HENÜZ ONAYLANMADI (2026-08-08 Hakan kararı — seçenek "c") ──
    //
    // Trendyol yazma uçları ASENKRON: submit anında "ne gönderdim" bellidir, "kabul edildi mi" belli DEĞİLDİR
    // ve batch REDDEDİLEBİLİR. Bu üç alan submit anında yazılır, batch COMPLETED olunca LastSent*'e TERFİ eder,
    // FAILED olunca TEMİZLENİR.
    //
    // Neden ayrı alan (reddedilen iki alternatif):
    //  (a) "Finalizasyonda satırları yeniden kur" — ürün o arada değiştiyse GÖNDERİLMEMİŞ değerler
    //      "gönderildi" diye yazılırdı: hatasız, logsuz, yalnız yanlış. Bu projenin en pahalı hata sınıfı.
    //  (b) "Doğrudan LastSent*'e yaz, FAILED'da geri al" — reddedilen gönderim, geri alma anına kadar
    //      "senkron" görünürdü; üstelik geri alma da başarısız olabilir. Kayıt yalan söyleyebilir hâle gelirdi.
    // (c) ile "ne gönderdim" sorusunun cevabı KAYITTA durur ve hiçbir aşamada tahmin edilmez.
    //
    // Şema notu: SKU satırı JSON kolonunda saklandığı için (OwnsMany + ToJson) bu alanlar migration İSTEMEZ.

    /// <summary>Gönderilen ama batch'i henüz sonuçlanmamış adet.</summary>
    public int? PendingSentQuantity { get; set; }

    /// <summary>Gönderilen ama batch'i henüz sonuçlanmamış listPrice.</summary>
    public decimal? PendingSentListPrice { get; set; }

    /// <summary>Gönderilen ama batch'i henüz sonuçlanmamış salePrice.</summary>
    public decimal? PendingSentSalePrice { get; set; }

    // İÇERİK BEKLEYENLERİ (2026-08-14): PushHistory satırının Title/VariantOptions/Images alanları finalize anında
    // ancak SUBMIT anında saklanmış değerden yazılabilir — finalize'da yeniden hesaplamak "göndermediğini
    // gönderdim diye yazma" hatasına girer (yukarıdaki (a) alternatifinin reddi ile aynı gerekçe).

    /// <summary>Gönderilen başlık (batch sonuçlanınca PushHistory'ye yazılır; yalnız FullPush doldurur).</summary>
    public string? PendingSentTitle { get; set; }

    /// <summary>Gönderilen eksen çiftleri, "Ad=Değer; Ad2=Değer2" biçiminde (PushHistory VariantOptions kaynağı).</summary>
    public string? PendingSentOptions { get; set; }

    /// <summary>FİİLEN gönderilen görsel MediaId'leri, virgüllü Guid metni ("id,id" — SIRALI, ilk = vitrin;
    /// yüklenemeyip düşen görsel listede YOKTUR).</summary>
    public string? PendingSentMediaIds { get; set; }

    public SalesChannelTrTrendyolProductSku()
    {
    }

    public SalesChannelTrTrendyolProductSku(Guid productVariantId, string barcode, string stockCode)
    {
        ProductVariantId = productVariantId;
        Barcode = barcode;
        StockCode = stockCode;
    }
}

/// <summary>Push edilecek varyant adayı — <see cref="SalesChannelTrTrendyolProduct.ReconcileSkus"/> girdisi
/// (varyant kimliği + kodu + varianter attribute id çiftleri).</summary>
public sealed record TrendyolSkuPushCandidate(
    Guid VariantId,
    string VariantCode,
    IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute> VarianterAttributes);

/// <summary>Mutabakat sapması — <see cref="SalesChannelTrTrendyolProduct.ReconcileObservedSkuState"/> çıktısı:
/// SKU tabanının eski (yerel tahmin) ve yeni (kanal gözlemi) değerleri. Yalnız log içindir; kalıcı değildir
/// (delil defteri push'a aittir, gözleme değil).</summary>
public sealed record TrendyolSkuObservedDrift(
    string Barcode,
    int? LocalQuantity,
    decimal? LocalListPrice,
    decimal? LocalSalePrice,
    int? ObservedQuantity,
    decimal? ObservedListPrice,
    decimal? ObservedSalePrice);

/// <summary>
/// Trendyol ürün listelemesi — bir ERP <see cref="Integration.TradeXpress.Products.Product"/>'ın belirli bir Trendyol
/// satış kanalında (SalesChannelTrTrendyol) listelenmesi. <b>Company-owned + per-tenant</b>. Trendyol'a ASENKRON
/// gönderilir (submit → <see cref="BatchRequestId"/>; durum ayrıca batch-request sorgusuyla çekilir). Kanalın KENDİ
/// kimliğiyle push edilir; varyantlar Trendyol item'larına (barcode/stockCode) eşlenir. <see cref="ProductMainId"/>
/// = varyant grup anahtarı ("{ÜrünKodu}-{SequenceNo}", frozen). Aynı kanalda aynı ürün için ÇOK kayıt olabilir
/// (2026-07-07); kanal SET-ONCE.
/// </summary>
public class SalesChannelTrTrendyolProduct : FullAuditedAggregateRoot<Guid>, IMultiTenant, ICompanyOwned
{
    #region Constructors

    protected SalesChannelTrTrendyolProduct()
    {
    }

    public SalesChannelTrTrendyolProduct(
        Guid companyId,
        Guid salesChannelId,
        Guid productId,
        string productMainId,
        int sequenceNo,
        string? categoryId,
        string brandId)
    {
        SetCompany(companyId);
        SetSalesChannel(salesChannelId);
        SetProduct(productId);
        SetProductMainId(productMainId, sequenceNo);
        SetCategory(categoryId, null);
        SetBrand(brandId, null);
        IsActive = true;
    }

    #endregion

    #region Properties

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Sahip şirket (güvenlik sınırı, set-once).</summary>
    public virtual Guid CompanyId { get; protected set; }

    /// <summary>Sahip Trendyol satış kanalı (set-once; kanalın kimliğiyle push edilir).</summary>
    public virtual Guid SalesChannelId { get; protected set; }

    /// <summary>Listelenen ERP ürünü (set-once; id-only, nav yok).</summary>
    public virtual Guid ProductId { get; protected set; }

    /// <summary>Trendyol varyant grup anahtarı (productMainId) — KAYIT-BAZLI benzersiz ("{ÜrünKodu}-{SequenceNo}").
    /// Set-once/FROZEN: sonradan ürün kodu değişse bile sabit kalır ki ikinci listeleme çakışmasın ve onaylı üründe
    /// DEĞİŞTİRİLEMEZ olan bu alan aynı uzak gruba gitsin.</summary>
    public virtual string ProductMainId { get; protected set; } = null!;

    /// <summary>Kayıt sırası (aynı ürün+kanal içinde; silinmişler DAHİL max+1 üretilir). Barcode/productMainId
    /// eklerinde de kullanılır ("{VaryantKodu}-{SequenceNo}") — Trendyol'da satıcı-geneli çakışmasın.</summary>
    public virtual int SequenceNo { get; protected set; }

    /// <summary>Trendyol kategori id'si (numerik; string tutulur). OPSİYONEL (2026-07-11 "gevşek kategori" kararı:
    /// pazaryeri kayıtlarında kategori eksik olabilir) — boş kalabilir; Trendyol'a push'ta zorunluluk dostane
    /// fail-fast ile aranır (kategorisiz kayıt gönderilemez).</summary>
    public virtual string? CategoryId { get; protected set; }

    /// <summary>Kategori görüntü adı (opsiyonel; UI kolaylığı).</summary>
    public virtual string? CategoryName { get; protected set; }

    /// <summary>Trendyol marka id'si (numerik; string tutulur — Trendyol zorunlu; onaylıda değiştirilemez).</summary>
    public virtual string BrandId { get; protected set; } = null!;

    /// <summary>Marka görüntü adı (marka arama sonucundan; opsiyonel).</summary>
    public virtual string? BrandName { get; protected set; }

    /// <summary>KDV oranı (Trendyol vatRate; %) — <b>varsayılanı YOK</b>. Eskiden ctor'da sessizce 20
    /// atanıyordu; bu kıymetli madende YANLIŞTI (maden teslimi %0 + istisna faturası, işçilik %20) ve kullanıcı
    /// hiçbir şeye dokunmazsa yanlış oran push ediliyordu (2026-08-03 Hakan düzeltmesi). Boşsa push anında
    /// ÜRÜNÜN oranı devralınır; o da boşsa push fail-fast reddedilir.</summary>
    public virtual int? VatRate { get; protected set; }

    /// <summary>Trendyol kargo firması id'si (cargoCompanyId) — REZERVE: Trendyol V2 create şemasında yer almadığı
    /// için push'a KONMAZ (kargo panel/satıcı seviyesi); ileride shipment-provider referansı netleşirse kullanılır.</summary>
    public virtual int? CargoCompanyId { get; protected set; }

    /// <summary>Desi/hacimsel ağırlık (Trendyol dimensionalWeight; opsiyonel).</summary>
    public virtual decimal? DimensionalWeight { get; protected set; }

    /// <summary>Kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public virtual string? Description { get; protected set; }

    /// <summary>Kargoya teslim süresi (gün) — Trendyol deliveryOption.deliveryDuration (opsiyonel).
    /// <see cref="FastDeliveryType"/> doluysa 1 olmalıdır.</summary>
    public virtual int? DeliveryDuration { get; protected set; }

    /// <summary>Hızlı teslimat tipi (opsiyonel). Doluysa <see cref="DeliveryDuration"/>=1 zorunludur.</summary>
    public virtual TrendyolFastDeliveryType? FastDeliveryType { get; protected set; }

    /// <summary>Trendyol kategori attribute değerleri (id-bazlı; owned → JSON kolonu "Attributes" — S6 tip rename'i
    /// şemayı DEĞİŞTİRMEZ).</summary>
    public virtual List<SalesChannelTrTrendyolProductCategoryAttribute> Attributes { get; protected set; } = new();

    /// <summary>Varyant-başına Trendyol SKU kimlik satırları (owned → JSON) — barcode dondurma + contentId + push
    /// snapshot'ı. Satır SİLİNMEZ (varyant yok olsa da Trendyol'da yaşıyor olabilir; emeklilik ileride).
    /// İKİ dolum yolu vardır: (1) PUSH — barcode YEREL üretilir ("{VaryantKodu}-{SequenceNo}", <see cref="BuildBarcode"/>)
    /// ve ilk başarılı push'ta dondurulur; (2) IMPORT (<see cref="UpsertImportedSku"/>) — barcode REMOTE'tan gelir
    /// (Trendyol'da zaten yaşayan değer) ve DOĞDUĞU GİBİ dondurulur; yerel üretim bu satıra HİÇ uygulanmaz
    /// (sonraki push'lar dondurulmuş remote barcode'u aynen kullanır — çatışma yok).</summary>
    public virtual List<SalesChannelTrTrendyolProductSku> Skus { get; protected set; } = new();

    // ── Uzak (Trendyol'daki) kayıt görüntüsü — IMPORT ile dolar, salt bilgi (push'a girmez) ──

    /// <summary>TRENDYOL'un varyant grup anahtarı (satıcının pazaryerine girdiği <c>productMainId</c>) — bizim
    /// ürettiğimiz kayıt-bazlı <see cref="ProductMainId"/>'den ("{ÜrünKodu}-{SequenceNo}", frozen) TAMAMEN AYRI:
    /// bizimki push kimliğidir ve bizde üretilir; bu alan ise pazaryerindeki MEVCUT kaydın kimliğidir ve import'un
    /// kanal-kaydı eşleşme anahtarıdır (ikinci import aynı kaydı bulur, dublike üretmez).</summary>
    public virtual string? RemoteProductMainId { get; protected set; }

    /// <summary>Uzak kayıt Trendyol tarafından ONAYLI mı (listing approved). null = henüz import edilmedi/bilinmiyor.</summary>
    public virtual bool? RemoteApproved { get; protected set; }

    /// <summary>Uzak kayıt SATIŞTA mı (onSale). null = henüz import edilmedi/bilinmiyor.</summary>
    public virtual bool? RemoteOnSale { get; protected set; }

    /// <summary>Uzak kayıttaki liste fiyatı (listPrice; indirim öncesi referans). Import görüntüsü — push fiyat
    /// zinciri StockItem override'larından yürür, bu alan zinciri ETKİLEMEZ.</summary>
    public virtual decimal? ListPrice { get; protected set; }

    /// <summary>Pazaryerinin KENDİ görsel adresleri (import görüntüsü; SIRALI — ilk vitrin). Kanal görseli bir
    /// kez alıp kendi CDN'ine taşır; bu liste "kanal şu an hangi görselleri gösteriyor"un cevabıdır ve push'un
    /// yeniden-kullanım dalını besler: görsel seti değişmediyse geçici link yerine BU adresler gönderilir —
    /// aksi hâlde her push kanala aynı görseli yeniden yutturur (CLAUDE.md §6 geçici-link akışı, dönüş ayağı).</summary>
    public virtual List<string> RemoteImageUrls { get; protected set; } = new();

    /// <summary><see cref="RemoteImageUrls"/> okunduğu anda ürünün DAM'daki görsel seti (SIRALI MediaId'ler) — kanal
    /// adreslerinin HANGİ yerel sete karşılık geldiğini söyler. Push'un yeniden-kullanım dalı bugünkü seti
    /// BUNUNLA kıyaslar; PushHistory ile değil. PushHistory ile kıyas bayat kanal adresini geri gönderebilirdi:
    /// import (kanalda A) → kullanıcı görseli B yapar → push#1 B'yi geçici linkle gönderir, PushHistory B →
    /// import koşmadan push#2: PushHistory B == bugün B ama RemoteImageUrls hâlâ A → kanala A gider, B geri
    /// alınır. Bu alan o sırayı kapatır: A adresleriyle birlikte yazılan set A'dır, bugün B ≠ A → geçici link yolu.</summary>
    public virtual List<Guid> RemoteImageMediaIds { get; protected set; } = new();

    /// <summary>Import her koşuşta uzak görsel listesini TAZELER (beyandır — null/boş da beyandır; koruma
    /// semantiği yok: kanal görselsiz diyorsa görselsizdir). Adreslerle birlikte o anki yerel görsel seti de
    /// <see cref="RemoteImageMediaIds"/>'e yazılır — ikisi ancak birlikte anlamlıdır. Adres sayısı ve uzunluğu
    /// import sınırında zaten kırpılmış gelir (bkz. Import <c>SafeRemoteImageUrls</c>); burada yalnız boş/beyaz elenir.</summary>
    public virtual void SetRemoteImageUrls(IEnumerable<string>? urls, IEnumerable<Guid>? localMediaIds)
    {
        RemoteImageUrls = urls?
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToList() ?? new List<string>();
        RemoteImageMediaIds = localMediaIds?.ToList() ?? new List<Guid>();
    }

    // ── Trendyol senkron durumu (async submit sonrası) ──
    /// <summary>Trendyol'un döndürdüğü batch istek kimliği (durum bununla sorgulanır).</summary>
    public virtual string? BatchRequestId { get; protected set; }

    /// <summary>Son gönderilen batch işlem tipi (OnBoarding/Update/InventoryUpdate) — hangi işlemin sonucu ayırt edilir.</summary>
    public virtual string? LastBatchRequestType { get; protected set; }

    /// <summary>Son bilinen batch/işlem durumu (PROCESSING/COMPLETED/FAILED ...).</summary>
    public virtual string? Status { get; protected set; }

    /// <summary>Son batch sonucundaki başarısız kalem sayısı (kısmi-hata sinyali).</summary>
    public virtual int? FailedItemCount { get; protected set; }

    public virtual DateTime? LastSyncedAt { get; protected set; }

    /// <summary>Son push/durum hatası (başarısızsa dolu, başarıda temizlenir).</summary>
    public virtual string? LastError { get; protected set; }


    // ── Push emniyet alanları (kurallar ChannelPushGuard'da; N11 ile BİREBİR aynı anlam) ──
    /// <summary>Kanalda GÖSTERİLMEYEN stok payı (opsiyonel) — push satırının nihai adedinden düşülür.</summary>
    public virtual int? SafetyStock { get; protected set; }

    /// <summary>Push fiyat TABANI (opsiyonel) — altına düşen fiyatta ürünün push'u durur.</summary>
    public virtual decimal? MinPrice { get; protected set; }

    /// <summary>Push fiyat TAVANI (opsiyonel) — üstüne çıkan fiyatta ürünün push'u durur.</summary>
    public virtual decimal? MaxPrice { get; protected set; }

    public virtual bool IsActive { get; protected set; }

    #endregion

    #region Methods

    /// <summary>Emniyet payı (opsiyonel; negatif reddedilir). Kural <see cref="ChannelPushGuard"/>'dadır.</summary>
    public virtual void SetSafetyStock(int? safetyStock)
    {
        SafetyStock = ChannelPushGuard.NormalizeSafetyStock(safetyStock);
    }

    /// <summary>Push fiyat bandı (opsiyonel; negatif sınır ve min&gt;max reddedilir). Tek uçlu bant meşrudur.</summary>
    public virtual void SetPriceBand(decimal? minPrice, decimal? maxPrice)
    {
        (MinPrice, MaxPrice) = ChannelPushGuard.NormalizePriceBand(minPrice, maxPrice);
    }

    /// <summary>Kategori OPSİYONEL (2026-07-11): boş/null → NULL yazılır (push'ta fail-fast aranır);
    /// doluysa uzunluk guard'ı uygulanır.</summary>
    public virtual void SetCategory(string? categoryId, string? categoryName)
    {
        CategoryId = StringFieldGuard.EnsureOptionalText(
            categoryId, nameof(CategoryId), 1, TrendyolProductConsts.CategoryIdMaxLength);
        CategoryName = StringFieldGuard.EnsureOptionalText(
            categoryName, nameof(CategoryName), 1, TrendyolProductConsts.CategoryNameMaxLength);
    }

    public virtual void SetBrand(string brandId, string? brandName)
    {
        BrandId = StringFieldGuard.EnsureRequiredText(
            brandId, nameof(BrandId), 1, TrendyolProductConsts.BrandIdMaxLength);
        BrandName = StringFieldGuard.EnsureOptionalText(
            brandName, nameof(BrandName), 1, TrendyolProductConsts.BrandNameMaxLength);
    }

    /// <summary>KDV oranı (opsiyonel; boş = ürünün oranı devralınacak). Dolu ise yürürlükteki oranlardan
    /// biri olmalı — serbest 0–100 aralığı KABUL EDİLMEZ (eski davranış uydurma oranın geçmesine izin veriyordu).</summary>
    public virtual void SetVatRate(int? vatRate)
    {
        if (vatRate is { } rate && !ProductConsts.AllowedVatRates.Contains(rate))
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:VatRateInvalid").WithData("VatRate", rate);
        }

        VatRate = vatRate;
    }

    public virtual void SetCargoCompany(int? cargoCompanyId)
    {
        if (cargoCompanyId is { } value && value < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:CargoCompanyInvalid");
        }

        CargoCompanyId = cargoCompanyId;
    }

    public virtual void SetDimensionalWeight(decimal? dimensionalWeight)
    {
        if (dimensionalWeight is { } value && value < 0)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DimensionalWeightInvalid");
        }

        DimensionalWeight = dimensionalWeight;
    }

    /// <summary>Kanal-özel açıklama (HTML; opsiyonel). Boşsa push'ta ürün açıklaması devralınır.</summary>
    public virtual void SetDescription(string? description)
    {
        Description = StringFieldGuard.EnsureOptionalText(
            description, nameof(Description), 1, TrendyolProductConsts.DescriptionMaxLength);
    }

    /// <summary>Teslimat seçeneği (opsiyonel). Hızlı teslimat tipi seçildiyse gün süresi 1 olmalıdır (Trendyol kuralı).</summary>
    public virtual void SetDeliveryOption(int? deliveryDuration, TrendyolFastDeliveryType? fastDeliveryType)
    {
        if (deliveryDuration is { } days && days < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:DeliveryDurationInvalid");
        }

        if (fastDeliveryType is not null && deliveryDuration != 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:FastDeliveryRequiresOneDay");
        }

        DeliveryDuration = deliveryDuration;
        FastDeliveryType = fastDeliveryType;
    }

    public virtual void SetActive(bool value)
    {
        IsActive = value;
    }

    public virtual void SetAttributes(IEnumerable<SalesChannelTrTrendyolProductCategoryAttribute>? attributes)
    {
        Attributes = (attributes ?? Enumerable.Empty<SalesChannelTrTrendyolProductCategoryAttribute>())
            .Where(a => a.AttributeId > 0)
            .Select(a => new SalesChannelTrTrendyolProductCategoryAttribute(
                a.AttributeId,
                a.AttributeValueId,
                string.IsNullOrWhiteSpace(a.CustomValue) ? null : a.CustomValue!.Trim()))
            .ToList();
    }

    /// <summary>Varyant barcode'u — kayıt-scoped: İLK listeleme ÇIPLAK varyant kodunu taşır, aynı ürünün ikinci
    /// listelemesinden itibaren "-{SequenceNo}" son eki ayırır (satıcı-geneli barcode çakışmaz). Kural
    /// <see cref="ChannelSequenceCode"/>'da (SSOT) — "-1" üretilmez.</summary>
    public virtual string BuildBarcode(string variantCode)
    {
        return ChannelSequenceCode.Compose(variantCode, SequenceNo);
    }

    /// <summary>Her varyanta gidecek barcode'u belirler — <b>entity'yi MUTASYONA UĞRATMAZ</b> (push ÖNCESİ güvenli
    /// çağrı): mevcut dondurulmuş satır barcode'unu tercih eder, eşleşme yoksa O ANKİ koddan üretir. Push başarısız
    /// olsa bile yeni satır persist edilmez (barcode ancak başarılı push'ta <see cref="ReconcileSkus"/> ile
    /// kalıcılaşır) — böylece "hiç Trendyol'a ulaşmamış bayat barcode" DB'ye donmaz.</summary>
    public virtual IReadOnlyDictionary<Guid, string> PlanBarcodes(IReadOnlyList<TrendyolSkuPushCandidate> candidates)
    {
        var assignment = AssignSkus(candidates, allowCreate: false);
        return candidates.ToDictionary(
            c => c.VariantId,
            c => assignment[c.VariantId]?.Barcode ?? BuildBarcode(c.VariantCode));
    }

    /// <summary>Push edilecek varyant setini kalıcı SKU satırlarıyla eşler + eksikleri kurar (BAŞARILI push
    /// SONRASI çağrılır) — varyant başına satır döner. Eşleme sırası (çalınma olmasın diye TÜM set üzerinden
    /// aşamalı): (1) ProductVariantId birebir; (2) dondurulmuş barcode = adayın üreteceği kod (synchronizer varyantı
    /// silip AYNI kodla yeniden üretti); (3) varianter attribute id imzası (kod da değiştiyse son ağ — aynı seçenek
    /// kombinasyonu = aynı uzak SKU); (4) hiçbiri yoksa YENİ satır (barcode O ANKİ varyant kodundan üretilir ve
    /// DONDURULUR). Yeniden bağlanan satırın ProductVariantId'si güncellenir; Barcode ASLA değişmez.</summary>
    public virtual IReadOnlyDictionary<Guid, SalesChannelTrTrendyolProductSku> ReconcileSkus(IReadOnlyList<TrendyolSkuPushCandidate> candidates)
    {
        var assigned = AssignSkus(candidates, allowCreate: true);

        // VARIANTER İMZASI BURADA KALICILAŞIR (2026-08-14 düzeltmesi): doğrulayıcı imzayı adaya koyuyordu ama
        // hiçbir yol snapshot'a yazmıyordu — 3. aşama (imza yeniden-bağlama) ölü koddu: eşleşecek snapshot hiç
        // oluşmuyordu. Trendyol'da RecordSkuPush push yolunda çağrılmaz (N11'den farklı: batch asenkron; LastSent*
        // finalize'da terfi eder), dolayısıyla imzanın kalıcılaşacağı tek an reconcile'dır. Boş imzalı aday
        // (önizleme/imza üretilmemiş) mevcut snapshot'ı EZMEZ — bilinen imzayı bilinmezle silmek yeniden-bağlamayı
        // körleştirirdi.
        foreach (var candidate in candidates)
        {
            if (candidate.VarianterAttributes.Count > 0 && assigned.TryGetValue(candidate.VariantId, out var sku) && sku is not null)
            {
                sku.AttributeSnapshot = candidate.VarianterAttributes
                    .Select(a => new SalesChannelTrTrendyolProductSkuAttribute(a.AttributeId, a.AttributeValueId))
                    .ToList();
            }
        }

        return assigned.ToDictionary(kv => kv.Key, kv => kv.Value!);
    }

    /// <summary>Başarılı push SONRASI gönderilen SKU verisini kaydeder (dirty-tracking + imza snapshot'ı). Push
    /// başarısızsa çağrılmaz — LastSent* yalnız Trendyol'a GERÇEKTEN ulaşan değerleri yansıtır.</summary>
    public virtual void RecordSkuPush(
        string barcode,
        int quantity,
        decimal? listPrice,
        decimal? salePrice,
        IEnumerable<SalesChannelTrTrendyolProductSkuAttribute> snapshot)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return;
        }

        sku.LastSentQuantity = quantity;
        sku.LastSentListPrice = listPrice;
        sku.LastSentSalePrice = salePrice;
        sku.AttributeSnapshot = snapshot
            .Select(a => new SalesChannelTrTrendyolProductSkuAttribute(a.AttributeId, a.AttributeValueId))
            .ToList();
    }

    /// <summary>GÜNLÜK MUTABAKAT — SKU tabanını kanalın FİİLEN bildirdiği adet/fiyata çeker (2026-08-21).
    ///
    /// <para><b>Semantik:</b> <c>LastSent*</c> "kanalda fiilen ne var" bilgisinin en iyi TAHMİNİDİR — normalde
    /// son başarılı gönderimimiz. Satıcı panelinden elle değişiklik ya da kaçan bir batch o tahmini gözlemden
    /// koparır; dirty-check <c>LastSent</c>'e baktığı için "değişiklik yok" der ve sapma SONSUZA DEK kalırdı.
    /// Bu metot tahmini GÖZLEMLE düzeltir; doğruyu kanala geri yazan şey normal senkron turudur (otorite devri:
    /// kanalda elle yapılan değişiklik geçersizdir, değerleri sistem belirler).</para>
    ///
    /// <para><b>Push DEĞİLDİR:</b> PushHistory'ye satır düşülmez — biz bir şey göndermedik ("gönderdim kaydı
    /// fiilen giden setten yazılır" kuralı). <c>Pending*</c>/<c>AttributeSnapshot</c>/kimlik alanlarına da
    /// dokunulmaz. Kanalın BİLDİRMEDİĞİ alan (<c>null</c>) yereldekini DEĞİŞTİRMEZ — bilgisizlik gözlem değildir.</para>
    ///
    /// <para>Dönüş: SKU bilinmiyorsa ya da sapma yoksa <c>null</c>; sapma varsa eski→yeni değerler
    /// (çağıran Warning loglar).</para></summary>
    public virtual TrendyolSkuObservedDrift? ReconcileObservedSkuState(
        string barcode, int? observedQuantity, decimal? observedListPrice, decimal? observedSalePrice)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return null;
        }

        var quantityDrifted = observedQuantity is not null && sku.LastSentQuantity != observedQuantity;
        var listPriceDrifted = observedListPrice is not null && sku.LastSentListPrice != observedListPrice;
        var salePriceDrifted = observedSalePrice is not null && sku.LastSentSalePrice != observedSalePrice;
        if (!quantityDrifted && !listPriceDrifted && !salePriceDrifted)
        {
            return null;
        }

        var drift = new TrendyolSkuObservedDrift(
            sku.Barcode,
            sku.LastSentQuantity, sku.LastSentListPrice, sku.LastSentSalePrice,
            observedQuantity, observedListPrice, observedSalePrice);

        if (quantityDrifted)
        {
            sku.LastSentQuantity = observedQuantity;
        }

        if (listPriceDrifted)
        {
            sku.LastSentListPrice = observedListPrice;
        }

        if (salePriceDrifted)
        {
            sku.LastSentSalePrice = observedSalePrice;
        }

        return drift;
    }

    /// <summary>SUBMIT anında "ne gönderdim"i kaydeder — henüz onaylanmış SAYILMAZ.
    /// <c>LastSent*</c>'e DOKUNMAZ: batch reddedilirse dirty-check eski (doğru) tabanla çalışmaya devam eder.
    /// İçerik üçlüsü (başlık/eksen/görsel) yalnız FullPush'ta dolar; senkron null geçer — gönderilmeyeni
    /// yazmak yalan olurdu (PushHistory kuralı).</summary>
    public virtual void RecordPendingSkuPush(
        string barcode, int? quantity, decimal? listPrice, decimal? salePrice,
        string? title = null, string? optionsText = null, string? mediaIdsCsv = null)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return;
        }

        sku.PendingSentQuantity = quantity;
        sku.PendingSentListPrice = listPrice;
        sku.PendingSentSalePrice = salePrice;
        sku.PendingSentTitle = title;
        sku.PendingSentOptions = optionsText;
        sku.PendingSentMediaIds = mediaIdsCsv;
    }

    /// <summary>Batch COMPLETED → bekleyen değerler <c>LastSent*</c>'e TERFİ eder ve bekleme temizlenir.
    /// Bekleyeni olmayan SKU'ya dokunulmaz (o gönderime dahil değildi; tabanını ezmek yalan olurdu).
    /// <b>İdempotent:</b> ikinci çağrıda bekleyen kalmadığı için no-op.</summary>
    public virtual void PromotePendingSkuPushes()
    {
        foreach (var sku in Skus.Where(s => s.PendingSentQuantity is not null
                                            || s.PendingSentListPrice is not null
                                            || s.PendingSentSalePrice is not null))
        {
            sku.LastSentQuantity = sku.PendingSentQuantity;
            sku.LastSentListPrice = sku.PendingSentListPrice;
            sku.LastSentSalePrice = sku.PendingSentSalePrice;
            ClearPending(sku);
        }
    }

    /// <summary>Batch FAILED → bekleyenler ATILIR. <c>LastSent*</c> DEĞİŞMEZ, yani bir sonraki senkron
    /// aynı farkı yeniden görür ve yeniden gönderir — reddedilen gönderim sessizce "yapıldı" sayılmaz.</summary>
    public virtual void ClearPendingSkuPushes()
    {
        foreach (var sku in Skus)
        {
            ClearPending(sku);
        }
    }

    private static void ClearPending(SalesChannelTrTrendyolProductSku sku)
    {
        sku.PendingSentQuantity = null;
        sku.PendingSentListPrice = null;
        sku.PendingSentSalePrice = null;
        sku.PendingSentTitle = null;
        sku.PendingSentOptions = null;
        sku.PendingSentMediaIds = null;
    }

    /// <summary>Trendyol yanıtındaki içerik id'sini (productContentId) yerel satıra işler — content-bulk-update
    /// kimliği. Yanıtta olmayan alan yereldekini SİLMEZ.</summary>
    public virtual void ApplyRemoteContentId(string barcode, long? remoteContentId)
    {
        var sku = FindSku(barcode);
        if (sku is null)
        {
            return;
        }

        sku.RemoteContentId = remoteContentId ?? sku.RemoteContentId;
    }

    /// <summary>Async submit sonrası: batch id + işlem tipi + PROCESSING durumu işaretlenir (hata temizlenir).</summary>
    public virtual void MarkSubmitted(string? batchRequestId, string? batchRequestType, DateTime submittedAtUtc)
    {
        BatchRequestId = StringFieldGuard.EnsureOptionalText(
            batchRequestId, nameof(BatchRequestId), 1, TrendyolProductConsts.BatchRequestIdMaxLength);
        LastBatchRequestType = StringFieldGuard.EnsureOptionalText(
            batchRequestType, nameof(LastBatchRequestType), 1, TrendyolProductConsts.BatchRequestTypeMaxLength);
        Status = TrendyolProductConsts.ProcessingBatchStatus;
        FailedItemCount = null;
        LastSyncedAt = submittedAtUtc;
        LastError = null;
    }

    /// <summary>Batch durum sorgusu sonrası: durum + başarısız kalem sayısı + (varsa) hata mesajı işaretlenir.</summary>
    public virtual void MarkStatus(string? status, int? failedItemCount, string? error, DateTime syncedAtUtc)
    {
        Status = StringFieldGuard.EnsureOptionalText(status, nameof(Status), 1, TrendyolProductConsts.StatusMaxLength);
        FailedItemCount = failedItemCount;
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, TrendyolProductConsts.LastErrorMaxLength);
        LastSyncedAt = syncedAtUtc;
    }

    /// <summary>Import'ta uzak kayıt görüntüsünü işler (remote productMainId + onay/satış bayrakları + listPrice).
    /// Salt bilgi alanlarıdır — push kimliği <see cref="ProductMainId"/> ve fiyat zinciri DEĞİŞMEZ.</summary>
    public virtual void ApplyRemoteSnapshot(string? remoteProductMainId, bool? approved, bool? onSale, decimal? listPrice)
    {
        if (listPrice is { } value && value < 0m)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:ListPriceNegative");
        }

        RemoteProductMainId = StringFieldGuard.EnsureOptionalText(
            remoteProductMainId, nameof(RemoteProductMainId), 1, TrendyolProductConsts.ProductMainIdMaxLength);
        RemoteApproved = approved;
        RemoteOnSale = onSale;
        ListPrice = listPrice;
    }

    /// <summary>Import'tan gelen SKU kimlik satırını upsert eder — anahtar BARCODE (remote'tan gelir, DONDURULMUŞ;
    /// yerel "{Kod}-{Sıra}" üretimi bu satıra uygulanmaz). Var olan satırda barcode ASLA değişmez; varyant bağı /
    /// stockCode / contentId tazelenir. Yeni satır remote kimliğiyle doğar.</summary>
    /// <param name="remoteQuantity">Pazaryerinde GÖRÜNEN adet (import anı). Verilmezse mevcut değer KORUNUR —
    /// eski çağrı yolları (kimlik-only upsert) pazaryeri beyanını sıfırlamasın diye.</param>
    /// <param name="remoteListPrice">Pazaryerindeki liste fiyatı (import anı).</param>
    /// <param name="remoteSalePrice">Pazaryerindeki satış fiyatı (import anı).</param>
    public virtual void UpsertImportedSku(
        Guid productVariantId,
        string barcode,
        string stockCode,
        long? remoteContentId,
        TrendyolRemoteListingState? remoteState = null)
    {
        // Varyant bağı zorunlu — fail-fast konvansiyonu (SetProduct/SetSalesChannel ile simetrik guard).
        if (productVariantId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelTrTrendyolProductSku.ProductVariantId));
        }

        var normalizedBarcode = StringFieldGuard.EnsureRequiredText(
            barcode, nameof(SalesChannelTrTrendyolProductSku.Barcode), 1, TrendyolProductConsts.BarcodeMaxLength);
        var normalizedStockCode = StringFieldGuard.EnsureRequiredText(
            stockCode, nameof(SalesChannelTrTrendyolProductSku.StockCode), 1, TrendyolProductConsts.StockCodeMaxLength);

        var sku = FindSku(normalizedBarcode);
        if (sku is null)
        {
            sku = new SalesChannelTrTrendyolProductSku(productVariantId, normalizedBarcode, normalizedStockCode);
            Skus.Add(sku);
        }
        else
        {
            sku.ProductVariantId = productVariantId;   // yeniden-bağlama; barcode DONDURULMUŞ kalır
            sku.StockCode = normalizedStockCode;
        }

        sku.RemoteContentId = remoteContentId ?? sku.RemoteContentId;

        if (remoteState is null)
        {
            return;
        }

        // Pazaryerinin beyanı — verilmeyen alan MEVCUDU KORUR. Koşulsuz atamak, kimlik-only çağrılarda
        // (yeniden-bağlama) daha önce okunmuş gerçek değerleri sessizce null'a çevirirdi.
        sku.RemoteQuantity = remoteState.Quantity ?? sku.RemoteQuantity;
        sku.RemoteListPrice = remoteState.ListPrice ?? sku.RemoteListPrice;
        sku.RemoteSalePrice = remoteState.SalePrice ?? sku.RemoteSalePrice;

        sku.RemoteArchived = remoteState.Archived ?? sku.RemoteArchived;
        sku.RemoteLocked = remoteState.Locked ?? sku.RemoteLocked;
        sku.RemoteBlacklisted = remoteState.Blacklisted ?? sku.RemoteBlacklisted;
        sku.RemoteRejected = remoteState.Rejected ?? sku.RemoteRejected;
        sku.RemoteHasActiveCampaign = remoteState.HasActiveCampaign ?? sku.RemoteHasActiveCampaign;

        sku.RemoteCreatedAtUtc = remoteState.CreatedAtUtc ?? sku.RemoteCreatedAtUtc;
        sku.RemoteUpdatedAtUtc = remoteState.UpdatedAtUtc ?? sku.RemoteUpdatedAtUtc;

        // Gerekçe metinleri KANALIN KENDİ cümlesidir — yeniden yazılmaz (PushHistory'deki ErrorMessage
        // ile aynı felsefe). Uzunluk emniyeti İMPORT SINIRINDA (BuildRemoteState kırpar); buradaki guard
        // fail-fast kalır — sınırı aşan değer kırpılmadan gelirse hata gizlenmez. Bayrak false'a döndüğünde
        // gerekçe de temizlenir: kalkmış bir karalistenin gerekçesini ekranda bırakmak, çözülmüş bir sorunu
        // yaşıyor göstermek olurdu.
        sku.RemoteLockReason = ResolveRemoteReason(remoteState.Locked, remoteState.LockReason, sku.RemoteLockReason);
        sku.RemoteBlacklistReason = ResolveRemoteReason(remoteState.Blacklisted, remoteState.BlacklistReason, sku.RemoteBlacklistReason);
        sku.RemoteRejectReason = ResolveRemoteReason(remoteState.Rejected, remoteState.RejectReason, sku.RemoteRejectReason);

        sku.RemoteProductUrl = StringFieldGuard.EnsureOptionalText(
            remoteState.ProductUrl, nameof(SalesChannelTrTrendyolProductSku.RemoteProductUrl),
            1, TrendyolProductConsts.RemoteProductUrlMaxLength) ?? sku.RemoteProductUrl;

        // Eksen değerleri: null = bildirilmedi (mevcut korunur); BOŞ liste = "eksen yok" BEYANI (tek kalemli
        // grup) — temizler. Grup tekil kalınca eski eksen snapshot'ını taşımak, push'a bayat "Renk" göndermek olurdu.
        if (remoteState.AxisValues is not null)
        {
            sku.RemoteVariantAttributes = remoteState.AxisValues.ToList();
        }
    }

    // Bayrak açıkça KAPALI bildirildiyse gerekçe de düşer; bildirilmediyse mevcut korunur.
    private static string? ResolveRemoteReason(bool? flag, string? incoming, string? current)
    {
        if (flag == false)
        {
            return null;
        }

        var normalized = StringFieldGuard.EnsureOptionalText(
            incoming, nameof(SalesChannelTrTrendyolProductSku.RemoteBlacklistReason),
            1, TrendyolProductConsts.RemoteReasonMaxLength);

        return normalized ?? current;
    }

    /// <summary>Başarısız submit/sorgu sonrası hatayı kaydeder.</summary>
    public virtual void MarkSyncFailed(string? error, DateTime attemptedAtUtc)
    {
        LastError = StringFieldGuard.EnsureOptionalText(error, nameof(LastError), 1, TrendyolProductConsts.LastErrorMaxLength);
        LastSyncedAt = attemptedAtUtc;
    }

    /// <summary>BAYAT BATCH (2026-08-19): çok uzun süredir PROCESSING'te kalan gönderim artık beklenmez.
    /// <b>Status PROCESSING'ten ÇIKARILIR</b> — eski yol yalnız <see cref="MarkSyncFailed"/> çağırıyor,
    /// <see cref="Status"/>'u bırakıyordu; çifte-batch koruması ("PROCESSING iken yeni submit yok") o kaydı
    /// SONSUZA KADAR kilitliyor, "kilidi açar" diye yazılan yol kilidi açmıyordu. Bekleyen SKU değerleri
    /// atılır (kanıtlanmamış gönderim "gönderildi" sayılmaz), <see cref="BatchRequestId"/> KORUNUR — kullanıcı
    /// "Durumu Yenile" ile akıbeti hâlâ sorabilsin. <c>LastSent*</c> değişmez: bir sonraki senkron aynı farkı
    /// görüp yeniden gönderir.</summary>
    public virtual void MarkBatchStale(DateTime markedAtUtc)
    {
        ClearPendingSkuPushes();
        Status = TrendyolProductConsts.StaleBatchStatus;
        LastError = TrendyolProductConsts.StaleBatchError;
        LastSyncedAt = markedAtUtc;
    }

    public override string ToString()
    {
        return $"{ProductId} @ {SalesChannelId}";
    }

    // Ortak eşleme metodu (SSOT): PlanBarcodes (readonly, allowCreate=false) ve ReconcileSkus (allowCreate=true)
    // aynı çok-aşamalı deterministik atamayı paylaşır → plan ile commit AYNI barcode'u üretir.
    private Dictionary<Guid, SalesChannelTrTrendyolProductSku?> AssignSkus(IReadOnlyList<TrendyolSkuPushCandidate> candidates, bool allowCreate)
    {
        var map = new Dictionary<Guid, SalesChannelTrTrendyolProductSku?>();
        var claimed = new HashSet<SalesChannelTrTrendyolProductSku>();
        var pending = new List<TrendyolSkuPushCandidate>();

        // (1) VariantId birebir — hâlâ bağlı satırlar önce sahiplenilir ki imza eşlemesi onları çalamasın.
        foreach (var candidate in candidates)
        {
            var byId = Skus.FirstOrDefault(s => s.ProductVariantId == candidate.VariantId);
            if (byId is not null && claimed.Add(byId))
            {
                map[candidate.VariantId] = byId;
            }
            else
            {
                pending.Add(candidate);
            }
        }

        // (2) Dondurulmuş barcode eşleşmesi → (3) attribute id imzası → (4) yeni satır (yalnız allowCreate).
        foreach (var candidate in pending)
        {
            var candidateBarcode = BuildBarcode(candidate.VariantCode);
            var sku = Skus.FirstOrDefault(s =>
                          !claimed.Contains(s)
                          && string.Equals(s.Barcode, candidateBarcode, StringComparison.OrdinalIgnoreCase))
                      ?? MatchUnclaimedBySignature(candidate.VarianterAttributes, claimed);

            if (sku is null && allowCreate)
            {
                sku = new SalesChannelTrTrendyolProductSku(candidate.VariantId, candidateBarcode, candidate.VariantCode);
                Skus.Add(sku);
            }

            if (sku is not null)
            {
                sku.ProductVariantId = candidate.VariantId;   // yeniden-bağlama; barcode DONDURULMUŞ kalır
                claimed.Add(sku);
            }

            map[candidate.VariantId] = sku;
        }

        return map;
    }

    // Sahiplenilmemiş satırlar içinde varianter attribute id imzası eşleşmesi — aynı seçenek kombinasyonu = aynı uzak SKU.
    private SalesChannelTrTrendyolProductSku? MatchUnclaimedBySignature(
        IReadOnlyList<SalesChannelTrTrendyolProductSkuAttribute> attributes, HashSet<SalesChannelTrTrendyolProductSku> claimed)
    {
        if (attributes.Count == 0)
        {
            return null;   // imzasız aday belirsiz — yanlış satıra bağlanmaktansa yeni satır açılır
        }

        var signature = SignatureOf(attributes);
        return Skus.FirstOrDefault(s =>
            !claimed.Contains(s)
            && s.AttributeSnapshot.Count > 0
            && SignatureOf(s.AttributeSnapshot) == signature);
    }

    // Seçenek imzası (id-bazlı): (AttributeId, AttributeValueId) çiftleri id'ye göre sıralı birleştirilir. Trendyol
    // id-bazlı olduğu için N11'in tr-TR kültür normalizasyonu GEREKMEZ (saf sayı karşılaştırması).
    private static string SignatureOf(IEnumerable<SalesChannelTrTrendyolProductSkuAttribute> attributes)
    {
        return string.Join(
            '|',
            attributes
                .Select(a => $"{a.AttributeId}:{a.AttributeValueId}")
                .OrderBy(x => x, StringComparer.Ordinal));
    }

    private SalesChannelTrTrendyolProductSku? FindSku(string barcode)
    {
        return Skus.FirstOrDefault(s => string.Equals(s.Barcode, barcode, StringComparison.OrdinalIgnoreCase));
    }

    private void SetCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(CompanyId));
        }

        CompanyId = companyId;
    }

    private void SetSalesChannel(Guid salesChannelId)
    {
        if (salesChannelId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(SalesChannelId));
        }

        SalesChannelId = salesChannelId;
    }

    private void SetProduct(Guid productId)
    {
        if (productId == Guid.Empty)
        {
            throw new RequiredPropertyException(nameof(ProductId));
        }

        ProductId = productId;
    }

    // Trendyol varyant grup anahtarı + sıra — SET-ONCE/FROZEN (yalnız ctor'dan; sonradan değişirse uzak grup kimliği
    // kayar ve onaylı üründe productMainId DEĞİŞTİRİLEMEZ).
    private void SetProductMainId(string productMainId, int sequenceNo)
    {
        ProductMainId = StringFieldGuard.EnsureRequiredText(
            productMainId, nameof(ProductMainId), 1, TrendyolProductConsts.ProductMainIdMaxLength);
        if (sequenceNo < 1)
        {
            throw new BusinessException("TradeXpress:Trendyol:Product:SequenceNoInvalid");
        }

        SequenceNo = sequenceNo;
    }

    #endregion
}
