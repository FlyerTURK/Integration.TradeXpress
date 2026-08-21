using System;
using Integration.Framework.Base.Dtos;
using Integration.Framework.Base.Dtos.Interfaces;
using Integration.TradeXpress.SalesChannels;
using Volo.Abp.Application.Dtos;

namespace Integration.TradeXpress.SalesChannelProducts;

/// <summary>Birleşik kanal-ürün sorgusu (per-tenant, company-owned) — şirket kapsamını SUNUCU zorlar
/// (client <c>CompanyId</c> GÖNDERMEZ). Merkezi <see cref="ListRequestDto"/> standardına ek olarak bu
/// listenin kendi tipli eksenleri taşınır; bunlar kolon filtresi değil, ekranın hangi kesiti gösterdiğini
/// belirleyen anahtarlardır.</summary>
public class SalesChannelProductListRequestDto : ListRequestDto
{
    /// <summary>Tek bir satış kanalına daralt (null = tüm kanallar). Kanal edit formu bunu doldurur,
    /// standalone liste boş bırakır — iki listenin TEK farkı budur.</summary>
    public Guid? SalesChannelId { get; set; }

    /// <summary>Kanal TÜRÜNE daralt (null = tüm türler). <see cref="SalesChannelId"/> doluyken gereksizdir
    /// (kanal zaten bir türe aittir) ama çelişirlerse ikisi de uygulanır — sonuç boş küme olur, sessizce
    /// birini yok saymaktan dürüsttür.</summary>
    public SalesChannelType? ChannelType { get; set; }

    /// <summary>Tek bir ERP ürününe daralt (null = tümü) — "bu ürün hangi kanallarda duruyor" görünümü.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Nötr senkron durumu filtresi (null = hepsi). <b>Bellekte</b> uygulanır: durum saklanan bir
    /// kolon değil, üç kanalın alanlarından TÜRETİLİR (bkz. <see cref="ChannelProductSyncState"/>).</summary>
    public ChannelProductSyncState? SyncState { get; set; }
}

/// <summary>
/// Birleşik kanal-ürün grid satırı — ÜÇ kanalın (N11 · Trendyol · Etsy) kanal-ürün kayıtları TEK listede,
/// kanal yalnız bir kolon (<see cref="ChannelType"/>).
///
/// <para><b>"Senkronize olmuş/olmamış" AYRIMI YOKTUR:</b> liste kanala bağlanmış TÜM kayıtları gösterir.
/// Gönderilmemiş kayıt bu ekranın hatası değil KONUSUDUR — kullanıcının aradığı asıl satır çoğu zaman
/// odur ("kanala bağladım ama çıkmamış").</para>
///
/// <para><b>Satır HAFİFTİR:</b> kanal-ürünün asıl grafı (varyant override'ları, kategori özellikleri,
/// SKU'lar) burada TAŞINMAZ — düzenleme formu onu kendi tipli servisinden çeker. Birleşik listede graf
/// taşımak, üç ağır DTO'yu her sayfalamada üç tablodan çekmek demekti.</para>
/// </summary>
public class SalesChannelProductListDto : EntityDto<Guid>, IListDto<Guid>, IIsActive
{
    public Guid SalesChannelId { get; set; }

    /// <summary>Kanal türü (discriminator) — grid "Kanal" kolonu + düzenleme formunun hangi tipe ait
    /// olduğunun TEK kaynağı (satır tıklanınca doğru edit formu bununla seçilir).</summary>
    public SalesChannelType ChannelType { get; set; }

    /// <summary>Kanal kodu — AppService'te TEK BATCH çözülür (id-only referanstan).</summary>
    public string? SalesChannelCode { get; set; }

    /// <summary>Kanal adı — AppService'te TEK BATCH çözülür.</summary>
    public string? SalesChannelName { get; set; }

    /// <summary>Bağlı ERP ürünü.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Ürün kodu — TEK BATCH çözülür. Ürün silinmişse boş kalır (satır yine gösterilir:
    /// öksüz kanal kaydı gizlenecek değil GÖRÜNECEK bir sorundur).</summary>
    public string? ProductCode { get; set; }

    /// <summary>Ürün adı — TEK BATCH çözülür.</summary>
    public string? ProductName { get; set; }

    /// <summary>Kanaldaki satıcı kodu — kanal başına farklı alandan gelir (N11 <c>SellerCode</c>,
    /// Trendyol <c>ProductMainId</c>, Etsy <c>SellerSkuBase</c>); üçü de "bu kaydın kanaldaki kimliği"
    /// rolünü oynar.</summary>
    public string? ChannelProductCode { get; set; }

    /// <summary>Kanal kategorisinin KÖKTEN TAM YOLU — "Kozmetik &gt; Cilt Bakımı &gt; Göz Makyaj Temizleyici"
    /// (<c>ChannelCategoryPathResolver</c>). Yaprak adı tek başına hangi dalda olunduğunu söylemez ve yaprak
    /// adları ağaç içinde benzersiz değildir; komisyon ile zorunlu öznitelikler dala bağlı olduğundan yol
    /// GEREKLİDİR. Ağaçta çözülemeyen bayat id'de kaydın dondurduğu yaprak adına düşülür (Etsy'de yaprak adı
    /// saklanmadığından orada boş kalır).</summary>
    public string? CategoryName { get; set; }

    /// <summary>
    /// KANALA ULAŞMIŞ SON FİYAT — SKU'ların <c>LastSent*</c> değerlerinden. Varyantlar farklı fiyattaysa
    /// <see cref="ChannelPriceMax"/> ile birlikte bir ARALIK oluşturur (eşitse ikisi aynıdır).
    ///
    /// <para><b>Neden bu alan güvenilir:</b> <c>LastSent*</c> yalnız BAŞARILI gönderimde terfi eder — yani
    /// "gönderdiğimizi sandığımız" değil, karşı tarafın kabul ettiği değerdir. Başarısız deneme burayı
    /// ilerletmez (o denemenin izi push geçmişi defterindedir).</para>
    ///
    /// <para><b><see cref="RemotePrice"/>'tan FARKLIDIR ve onun yerine geçmez:</b> o, pazaryerinin BİZE
    /// bildirdiği fiyattır (içe aktarım görüntüsü, yalnız Trendyol); bu ise BİZİM gönderdiğimizdir. İkisi
    /// ayrıştığında haber değeri taşıyan şey tam da o farktır — tek kolonda birleştirmek onu gizlerdi.</para>
    /// </summary>
    public decimal? ChannelPrice { get; set; }

    /// <summary>Kanala ulaşmış son fiyatın ÜST ucu (varyantlar farklı fiyattaysa). <see cref="ChannelPrice"/>
    /// ile eşitse tek fiyat vardır.</summary>
    public decimal? ChannelPriceMax { get; set; }

    /// <summary>KANALA ULAŞMIŞ SON ADET — SKU'ların <c>LastSentQuantity</c> TOPLAMI (kanalda görünen toplam
    /// stok). <c>null</c> = hiç başarılı gönderim olmamış; <c>0</c> ise meşru bir beyandır ("tükendi").</summary>
    public int? ChannelQuantity { get; set; }

    /// <summary>Pazaryerindeki kimlik (N11 ürün id · Trendyol ana ürün kodu · Etsy listing id) — METİN
    /// olarak taşınır çünkü üç kanalın tipi farklı. Boş = pazaryerinde karşılığı yok.</summary>
    public string? RemoteId { get; set; }

    /// <summary>Nötr senkron durumu (türetilir — bkz. <see cref="ChannelProductSyncState"/>).</summary>
    public ChannelProductSyncState SyncState { get; set; }

    /// <summary>Kanalın bildirdiği ham durum metni (N11 satış/onay durumu · Trendyol batch durumu ·
    /// Etsy listing state) — nötr enum'un KAYNAĞI, denetim için taşınır.</summary>
    public string? RemoteStatus { get; set; }

    /// <summary>Son senkron anı (UTC) — görüntüde kullanıcının yerel saatine çevrilir. <b>Aynı zamanda
    /// "biz gönderdik mi" kanıtıdır:</b> uzak kimlik içe aktarımda da dolar, bu timestamp yalnız başarılı
    /// gönderimde.</summary>
    public DateTime? LastSyncedAt { get; set; }

    /// <summary>PAZARYERİNDE GÖSTERİLEN fiyat (Trendyol liste fiyatı; içe aktarım görüntüsü).
    /// <para><b>N11'de <c>null</c> kalır ve bu bir eksiklik DEĞİLDİR:</b> N11 kanal kaydı pazaryeri fiyatını
    /// saklamıyor (board'u da göstermiyor). Boş hücre "bilmiyoruz" der; uydurulmuş bir sayı yazmak
    /// yanlış delil olurdu.</para></summary>
    public decimal? RemotePrice { get; set; }

    /// <summary>Pazaryerinde SATIŞTA mı (Trendyol). <c>null</c> = bilinmiyor — üç durumlu bilinçli:
    /// doğrulanmadan "satışta değil" demek yanlış karar verdirirdi.</summary>
    public bool? RemoteOnSale { get; set; }

    /// <summary>
    /// PAZARYERİ ENGELİ — kayıt kanalda neden satılamıyor (karaliste / red / kilit / arşiv). Kaydın
    /// SKU'ları arasındaki EN AĞIR engeldir: tek kalemi engelli bir kayıt "engelsiz" sayılamaz.
    ///
    /// <para><b>Neden ayrı bir kavram:</b> <see cref="RemoteOnSale"/> ile <c>approved</c> "satışta mı"yı
    /// söyler, "neden değil"i söylemez. Karalisteye alınmış bir kalem bizde aylarca "onaylı + satışta"
    /// göründü; gönderim karşı tarafta reddedildi ve sebebi hiçbir ekranda yoktu.</para>
    ///
    /// <para><b>Engel push'u DURDURMAZ</b> — bayraklar son içe aktarım anının snapshot'ıdır ve bayat bir
    /// bayrağa dayanıp gönderimi kesmek, çözülmüş bir sorunu kalıcı kılardı. Sistem uyarır, kullanıcı karar
    /// verir. Trendyol dışı kanallarda <c>None</c> kalır (o kanallar böyle bir beyan döndürmüyor).</para>
    /// </summary>
    public ChannelListingObstacle Obstacle { get; set; }

    /// <summary>Engelin PAZARYERİ GEREKÇESİ — kanalın kendi cümlesi, yeniden yazılmaz. Engel yoksa ya da
    /// gerekçe bildirilmemişse boş: engelin VARLIĞI ile GEREKÇESİ ayrı sorulardır.</summary>
    public string? ObstacleReason { get; set; }

    /// <summary>Kanalda AKTİF KAMPANYA var mı. Otonom fiyat güncellemesi bu kayda yazmadan önce kullanıcının
    /// görmesi gereken tek şey budur — kampanyalı fiyata müdahalenin sonucu bizde modellenmedi.
    /// <c>null</c> = bilinmiyor.</summary>
    public bool? HasActiveCampaign { get; set; }

    /// <summary>Kaydın pazaryerindeki sayfası — satırdan doğrudan gidilir. Çok kalemli kayıtta ilk kalemin
    /// adresidir (varyantlar aynı sayfada yaşar).</summary>
    public string? RemoteUrl { get; set; }

    /// <summary>Kalemin pazaryerinde SON DEĞİŞTİĞİ an (UTC; kayıttaki en yeni timestamp). <see cref="LastSyncedAt"/>
    /// sonrasındaysa kanalda bize ait olmayan bir müdahale olmuştur — bunun tek kanıtı budur.</summary>
    public DateTime? RemoteUpdatedAt { get; set; }

    /// <summary>Kaydın pazaryerinde İLK oluşturulduğu an (UTC; kalemler arasındaki en eski timestamp) — "kanalda ne
    /// zamandır var" sorusunun cevabı; içe aktarılmış ürünlerde bizim kaydımızdan eski olabilir.</summary>
    public DateTime? RemoteCreatedAt { get; set; }

    /// <summary>Son hata metni (varsa) — satır neden <see cref="ChannelProductSyncState.Failed"/> onu söyler.</summary>
    public string? LastError { get; set; }

    /// <summary>Kanal kaydının varyant/SKU adedi — "kaç satır gidiyor" göstergesi.</summary>
    public int SkuCount { get; set; }

    // ── Board alanları (ChannelProductBoardBuilder'dan; fiyatlandırma board'uyla AYNI kaynak) ──────────
    // Bu dört alan "bu üründe daha ne yapılacak?" sorusunun cevabıdır. Kanal-ürün listesine taşındılar
    // çünkü kullanıcı için iki soru AYRI değil: "kanalda ne var" ile "hangisi eksik" aynı ekranda okunur.

    /// <summary>Ürün görseli (DAM; varyant→kayıt-geneli fallback'li). Boş = görsel bağlanmamış —
    /// bu da bir bilgidir, boş hücre değil görünür bir işaretle gösterilir.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>ERP varyant sayısı (kanal SKU sayısından FARKLI: bu ürünün kaç varyantı var,
    /// <see cref="SkuCount"/> ise kanala kaç satır gidiyor).</summary>
    public int VariantCount { get; set; }

    /// <summary>Ürünün reçetesi VAR mı — içeriği değil VARLIĞI ölçülür (tek satır bile yeterli).
    /// Reçetesiz ürünün stoğu/maliyeti hesaplanamaz; kanaldaki en sık kök sorun budur.</summary>
    public bool HasRecipe { get; set; }

    /// <summary>ŞU AN satışa çıkabilecek varyant sayısı — "onaylanmış" değil "bugün geçer" sayısıdır
    /// (<c>VerifiedRecipeStamp</c>'in tazeliği de aranır). Reçete değişince düşer ve sebebi budur.</summary>
    public int ReadyVariantCount { get; set; }

    /// <summary>Satışa hazırlık kademesi — <see cref="HasRecipe"/> ve <see cref="ReadyVariantCount"/>'tan
    /// TÜRETİLİR ama gerçek bir alan olarak taşınır: grid ancak veri alanı olan kolonu GRUPLAYABİLİR
    /// (gerekçe <see cref="ChannelProductReadiness"/> özetinde). Varsayılan sıralama da bunu okur.</summary>
    public ChannelProductReadiness Readiness { get; set; }

    /// <summary>Görsel BAĞLI MI — <see cref="ImageUrl"/>'den türetilir. Ayrı alan olmasının tek sebebi
    /// gruplama: URL'e göre gruplamak her ürünü kendi grubuna koyardı; asıl sorulan soru "hangi ürünlerin
    /// görseli eksik" ve gruplanabilir olması gereken şey bu ikili cevaptır.</summary>
    public bool HasImage { get; set; }

    public bool IsActive { get; set; }

    public override string ToString()
    {
        return ChannelProductCode ?? ProductCode ?? Id.ToString();
    }
}

/// <summary>
/// PushHistory'nin TEK satırı — kanal-agnostik okuma modeli (append-only tabloların birleşik görüntüsü).
///
/// <para><b>Ne cevaplar:</b> "hangi fiyat/stok ne zaman gönderildi, ulaştı mı, ulaşmadıysa neden".
/// Otonom fiyat/stok güncellemesi devreye girdiğinde bu sorunun cevabı yalnız burada yaşar — kanal-ürün
/// kaydındaki <c>LastSent*</c> her turda üzerine yazıldığı için geçmişi tutmaz.</para>
///
/// <para><b>Kanal farkları alan seviyesinde eritilir:</b> SKU kimliği N11'de satıcı stok kodu, Trendyol'da
/// barkoddur → tek <see cref="SkuCode"/>; uzak referans N11'de task/ürün kimliği, Trendyol'da batch
/// kimliğidir → tek <see cref="RemoteReference"/>. <see cref="ListPrice"/> yalnız Trendyol'da doludur
/// (indirim liste/satış farkıdır); N11'de kavram yok, <c>null</c> kalır ve bu bir eksiklik DEĞİLDİR.</para>
/// </summary>
public class SalesChannelProductPushHistoryDto : EntityDto<Guid>
{
    /// <summary>Gönderim anı (UTC) — görüntüde kullanıcının yerel saatine çevrilir (CLAUDE.md §6).</summary>
    public DateTime PushedAtUtc { get; set; }

    /// <summary>Gönderimin türü (nötr).</summary>
    public ChannelPushKind Kind { get; set; }

    /// <summary>Sonuç — ulaştı mı ulaşmadı mı.</summary>
    public ChannelPushOutcome Outcome { get; set; }

    /// <summary>Başarısızlığın gerekçesi — KANALIN kendi mesajı. Başarıda <c>null</c>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gönderilen SKU kimliği (N11 satıcı stok kodu · Trendyol barkod).</summary>
    public string? SkuCode { get; set; }

    /// <summary>Gönderilen satış fiyatı — müşterinin gördüğü sayı.</summary>
    public decimal? SalePrice { get; set; }

    /// <summary>Gönderilen liste fiyatı (üstü çizili). Yalnız Trendyol'da anlamlı.</summary>
    public decimal? ListPrice { get; set; }

    /// <summary>Fiyatın para birimi — "150" tek başına delil değildir. Yalnız N11 taşır.</summary>
    public string? CurrencyType { get; set; }

    /// <summary>Gönderilen adet.</summary>
    public int? Quantity { get; set; }

    /// <summary>Gönderilen başlık — yalnız tam push'ta dolu (senkron içerik göndermez).</summary>
    public string? Title { get; set; }

    /// <summary>Karşı tarafla eşleştirme anahtarı (N11 task/ürün kimliği · Trendyol batch kimliği).</summary>
    public string? RemoteReference { get; set; }

    public override string ToString()
    {
        return $"{SkuCode}@{PushedAtUtc:O}";
    }
}
