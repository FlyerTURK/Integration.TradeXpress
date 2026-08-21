using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.Products;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy ürün LİSTELEME okuma istemcisi — Etsy Open API v3 (REST/JSON, api.etsy.com/v3). SALT-OKUMA (GET): mağazanın
/// AKTİF listelemelerini limit/offset sayfa döngüsüyle çeker (<c>getListingsByShop</c>, scope <c>listings_r</c>) +
/// her listelemenin inventory (offering) ve görsellerini birlikte getirir (<c>includes=Inventory,Images</c>; gelmezse
/// per-listing fallback). Etsy'ye SIFIR yazma. Kanal-agnostik <see cref="EtsyRemoteListing"/> döner. Auth: access token
/// <see cref="EtsyCredentials.ChannelId"/> üzerinden token sağlayıcıdan (refresh şeffaf) + <c>x-api-key</c> başlığı
/// (<see cref="EtsyOrderClient"/> ile AYNI kimlik/şablon). Defansif JSON okuma (alan yoksa/tipi farklıysa null).
///
/// <para><b>⚠ Alan adları Etsy v3 dokümanına göre yazılmıştır ancak bu oturumda CANLI DOĞRULANMADI</b> — endpoint/alan
/// varsayımları gerçek satıcı kimliğiyle teyit edilmelidir (yalnız bu dosyadaki sabitleri güncellemek yeterli;
/// model/AppService değişmez).</para>
/// </summary>
public interface IEtsyProductClient
{
    /// <summary>Mağazanın TÜM aktif listelemelerini (limit/offset sayfa döngüsü) çeker + her birinin inventory
    /// (offering seti) ve görsellerini doldurur (salt GET). İnventory/görsel tek çağrıda gelmezse per-listing
    /// fallback ile tamamlar.</summary>
    Task<IReadOnlyList<EtsyRemoteListing>> GetAllListingsAsync(
        EtsyCredentials credentials, int pageSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Mağazanın kargo profillerini (<c>getShopShippingProfiles</c>) salt GET ile çeker — push kargo-profili
    /// picker'ının beslemesi (Etsy'ye SIFIR yazma). Shop-scoped uç → Bearer token + <c>x-api-key</c> birlikte
    /// (taxonomy'nin aksine app-key yetmez; <see cref="GetAllListingsAsync"/> ile AYNI kimlik). Silinmiş profil
    /// (<c>is_deleted=true</c>) elenir; picker için yalnız kimlik + başlık döner.</summary>
    Task<IReadOnlyList<EtsyShippingProfileSummary>> GetShopShippingProfilesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Mağazanın iade politikalarını (<c>getShopReturnPolicies</c>, <c>GET .../shops/{shopId}/policies/return</c>)
    /// salt GET ile çeker — iade politikası picker'ının beslemesi (Etsy'ye SIFIR yazma). Shop-scoped uç → Bearer token +
    /// <c>x-api-key</c> (kargo profili ile AYNI kimlik). Etsy iade politikasının BAŞLIĞI YOKTUR → picker etiketi
    /// AppService'te (lokalize) iade/değişim + süre alanlarından türetilir; burada yalnız ham alanlar döner.</summary>
    Task<IReadOnlyList<EtsyReturnPolicySummary>> GetShopReturnPoliciesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Mağazanın dükkân bölümlerini (<c>getShopSections</c>, <c>GET .../shops/{shopId}/sections</c>) salt GET ile
    /// çeker — dükkân bölümü picker'ının beslemesi (Etsy'ye SIFIR yazma). Shop-scoped uç → Bearer token + <c>x-api-key</c>
    /// (kargo profili ile AYNI kimlik). Picker için yalnız kimlik (<c>shop_section_id</c>) + başlık (<c>title</c>) döner.</summary>
    Task<IReadOnlyList<EtsyShopSectionSummary>> GetShopSectionsAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Mağazada YENİ dükkân bölümü OLUŞTURUR (<c>createShopSection</c>, <c>POST .../shops/{shopId}/sections</c>,
    /// form-urlencoded <c>title</c>) — Etsy'ye YAZMA (yalnız kullanıcı formu doldurup kaydedince çağrılır). Oluşan bölümün
    /// özeti (id + title) döner. Auth kargo/section GET ile AYNI (Bearer + <c>x-api-key</c>).</summary>
    Task<EtsyShopSectionSummary> CreateShopSectionAsync(
        EtsyCredentials credentials, string title, CancellationToken cancellationToken = default);

    /// <summary>Mevcut dükkân bölümünün başlığını GÜNCELLER (<c>updateShopSection</c>,
    /// <c>PUT .../shops/{shopId}/sections/{shopSectionId}</c>, form-urlencoded <c>title</c>) — Etsy'ye YAZMA. Güncel
    /// bölümün özeti (id + title) döner.</summary>
    Task<EtsyShopSectionSummary> UpdateShopSectionAsync(
        EtsyCredentials credentials, long shopSectionId, string title, CancellationToken cancellationToken = default);

    /// <summary>Mağazada YENİ iade politikası OLUŞTURUR (<c>createShopReturnPolicy</c>,
    /// <c>POST .../shops/{shopId}/policies/return</c>, form-urlencoded <c>accepts_returns</c>/<c>accepts_exchanges</c> +
    /// kabul varsa <c>return_deadline</c> gün) — Etsy'ye YAZMA. Oluşan politikanın ham özeti döner (başlık yok; etiket
    /// AppService'te türetilir).</summary>
    Task<EtsyReturnPolicySummary> CreateReturnPolicyAsync(
        EtsyCredentials credentials, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default);

    /// <summary>Mevcut iade politikasını GÜNCELLER (<c>updateShopReturnPolicy</c>,
    /// <c>PUT .../shops/{shopId}/policies/return/{returnPolicyId}</c>, form-urlencoded aynı alanlar) — Etsy'ye YAZMA.
    /// Güncel politikanın ham özeti döner.</summary>
    Task<EtsyReturnPolicySummary> UpdateReturnPolicyAsync(
        EtsyCredentials credentials, long returnPolicyId, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default);

    /// <summary>Listelemenin VARYASYON FOTOĞRAFI bağlarını (<c>getListingVariationImages</c>,
    /// <c>GET .../shops/{shopId}/listings/{listingId}/variation-images</c>) salt GET ile çeker — hangi varyasyon
    /// değerine hangi fotoğrafın bağlandığı (Etsy'ye SIFIR yazma; bu dilimde YAZMA YOK). Mağaza kimliği
    /// <see cref="EtsyCredentials.ShopId"/>'den gelir (kanal kaydında saklı) — diğer shop-scoped uçlarla AYNI
    /// kimlik/hata deseni.
    ///
    /// <para><b>404/boş yanıt İSTİSNA DEĞİLDİR → BOŞ liste:</b> varyasyon fotoğrafı olmayan listeleme normaldir
    /// (fotoğraflar yalnız kayıt geneli galeride durur). Bunu hata saymak, mağazanın çoğunluğunu oluşturan normal
    /// listelemelerde içe aktarımı gürültüye boğardı.</para></summary>
    Task<IReadOnlyList<EtsyVariationImage>> GetVariationImagesAsync(
        EtsyCredentials credentials, long listingId, CancellationToken cancellationToken = default);

    /// <summary>Kanalın erişim token'ının GEÇERLİ ve bir mağazaya bağlı olduğunu doğrular (<c>getMe</c>,
    /// <c>GET .../users/me</c>, salt GET; Etsy'ye SIFIR yazma). Kurulum "auth" adımının kimlik ön-koşulunu teyit eder —
    /// başarılı yanıtta <c>user_id</c> (+ varsa <c>shop_id</c>) döner. Token yenileme <see cref="IEtsyTokenProvider"/>
    /// içinde şeffaf. Yanıt kimliksizse null.</summary>
    Task<EtsyIdentity?> VerifyIdentityAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default);
}

/// <summary>Mağaza kargo profili özeti (picker beslemesi) — <c>getShopShippingProfiles</c> <c>results[]</c> öğesinden
/// yalnız <see cref="Id"/> (<c>shipping_profile_id</c>) + <see cref="Title"/>. Gevşek referans: yerelde yalnız
/// <c>ShippingProfileId</c> saklarız, profil Etsy'de tanımlıdır.</summary>
public sealed record EtsyShippingProfileSummary(long Id, string Title);

/// <summary>Mağaza iade politikası özeti (picker beslemesi) — <c>getShopReturnPolicies</c> <c>results[]</c> öğesinden.
/// Etsy iade politikasının BAŞLIĞI YOKTUR (yanıtta yalnız <c>return_policy_id</c> + <c>accepts_returns</c>/
/// <c>accepts_exchanges</c>/<c>return_deadline</c> gelir) → görüntü etiketi bu ham alanlardan AppService'te (lokalize)
/// türetilir. Gevşek referans: yerelde yalnız <c>ReturnPolicyId</c> saklarız, politika Etsy'de tanımlıdır.</summary>
public sealed record EtsyReturnPolicySummary(long Id, int? ReturnDeadlineDays, bool AcceptsReturns, bool AcceptsExchanges);

/// <summary>Mağaza dükkân bölümü özeti (picker beslemesi) — <c>getShopSections</c> <c>results[]</c> öğesinden yalnız
/// <see cref="Id"/> (<c>shop_section_id</c>) + <see cref="Title"/>. Gevşek referans: yerelde yalnız <c>ShopSectionId</c>
/// saklarız, bölüm Etsy'de tanımlıdır.</summary>
public sealed record EtsyShopSectionSummary(long Id, string Title);

/// <summary>Etsy kimlik teyit sonucu (<c>getMe</c>) — token'ın çözdüğü kullanıcı (<c>user_id</c>) ve (varsa) bağlı
/// mağaza (<c>shop_id</c>). Kurulum "auth" adımı yalnız token'ın geçerliliğini doğrulamak için kullanır.</summary>
public sealed record EtsyIdentity(long UserId, long? ShopId);

/// <summary>Uzak (Etsy'deki) aktif listeleme — GERÇEK offering grafıyla (kartezyen DEĞİL; Etsy'nin girdiği set).
/// <see cref="ListingId"/> import'un kanal-kaydı idempotency anahtarıdır (<c>EtsyListingId</c>). Menşe alanları
/// (<see cref="WhoMade"/>/<see cref="WhenMade"/>) null = Etsy dönmedi → import ürün varsayılanını korur.</summary>
public sealed record EtsyRemoteListing(
    long ListingId,
    string Title,
    string? Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Materials,
    long? TaxonomyId,
    EtsyWhoMade? WhoMade,
    ProductMadePeriod? WhenMade,
    EtsyListingType ListingType,
    IReadOnlyList<EtsyRemoteImage> Images,
    string? CurrencyCode,
    IReadOnlyList<EtsyRemoteOffering> Offerings)
{
    /// <summary>Kayıt geneli galeri için DÜZ URL listesi — <see cref="Images"/>'ten TÜRETİLİR, ayrı bir yapıcı
    /// parametresi DEĞİLDİR (mevcut çağıranlar aynen çalışmaya devam eder).
    ///
    /// <para><b>Neden türetilmiş, neden "ikisini birlikte doldur" değil:</b> istemci eksik gelen görselleri
    /// per-listing fallback ile tamamlarken kaydı <c>listing with { Images = ... }</c> ile kopyalar. <c>with</c>
    /// kopya-yapıcısı ALANLARI kopyalar, başlatıcıları yeniden koşturmaz → iki ayrı alan tutulsaydı yeni kimlikli
    /// görsel seti yanına ESKİ (boş) URL listesi taşınır ve fark hiçbir yerde hata vermeden ürün galerisini
    /// boşaltırdı. Tek gerçek kaynak <see cref="Images"/>'tir; URL görünümü ondan okunur.</para></summary>
    public IReadOnlyList<string> ImageUrls
    {
        get
        {
            var urls = new List<string>(Images.Count);
            foreach (var image in Images)
            {
                urls.Add(image.Url);
            }

            return urls;
        }
    }
}

/// <summary>Uzak listeleme görseli — KİMLİKLİ (<c>listing_image_id</c>) + adres. Kimlik varyasyon fotoğrafı
/// eşleştirmesinin tek anahtarıdır (<see cref="EtsyVariationImage.ImageId"/>); Etsy kimliği döndürmezse
/// <see cref="ImageId"/> 0 kalır — görsel kayıt geneli galeriye yine iner ama varyanta BAĞLANAMAZ (uydurma
/// eşleşme yapılmaz).</summary>
public sealed record EtsyRemoteImage(long ImageId, string Url);

/// <summary>Listelemenin bir varyasyon fotoğrafı bağı (<c>getListingVariationImages</c>): hangi varyasyon
/// ekseninin (<see cref="PropertyId"/>) hangi değerine (<see cref="ValueId"/>) hangi fotoğrafın
/// (<see cref="ImageId"/>) bağlandığı.
///
/// <para><b>Etsy kısıtları (resmî v3):</b> bir listelemede fotoğraflar YALNIZ TEK bir varyasyon grubuna bağlanır
/// (dizide tek distinct <c>property_id</c>), en fazla 20 benzersiz seçenek fotoğraf taşıyabilir ve listeleme başına
/// en fazla 10 fotoğraf vardır.</para></summary>
public sealed record EtsyVariationImage(long PropertyId, long ValueId, long ImageId);

/// <summary>Uzak listelemenin BİR offering'i (= inventory <c>product</c>) — varyant kalemi. <see cref="EtsyProductId"/>
/// = Etsy inventory <c>product_id</c> (offering-düzeyi idempotency; <c>Sku.EtsyProductId</c>). <see cref="Properties"/>
/// = bu offering'in seçili varyant değerleri (name/value çiftleri; <c>property_values</c>'tan). Tek-varyant
/// listelemede boş.</summary>
public sealed record EtsyRemoteOffering(
    string? Sku,
    int Quantity,
    decimal? Price,
    bool IsEnabled,
    long EtsyProductId,
    IReadOnlyList<EtsyRemoteProperty> Properties);

/// <summary>Bir offering'in tek varyant ekseni seçimi (ör. Renk=Kırmızı) — <c>property_values</c> öğesinden
/// name + ilk değer, YANINDA Etsy'nin sayısal kimlikleri.
///
/// <para><see cref="PropertyId"/>/<see cref="ValueId"/> yalnız ZENGİNLEŞTİRMEDİR: varyant grafı bugünkü gibi
/// METİN (ad/değer) üzerinden kurulmaya devam eder — kimlikler gelmezse (eski/kısmi yanıt) <c>null</c> kalır ve
/// hiçbir mevcut davranış değişmez. Kimliği okumamızın tek sebebi varyasyon fotoğrafı eşleştirmesidir: Etsy o
/// bağı ADLA değil <c>property_id</c>/<c>value_id</c> ile verir.</para></summary>
public sealed record EtsyRemoteProperty(string Name, string Value, long? PropertyId = null, long? ValueId = null);
