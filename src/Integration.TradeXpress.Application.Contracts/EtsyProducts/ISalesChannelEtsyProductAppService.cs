using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy ürün listeleme — bir ERP ürününü bir Etsy kanalında listeler. Company-owned. Listeleme yapılandırması
/// (taksonomi/attribute/kargo profili/işleme süresi/kişiselleştirme/özel bilgi) bizde tutulur. N11
/// <c>ISalesChannelTrN11ProductAppService</c> ikizi — bu dilimde yalnız CRUD + ürün-merkezli drill; PUSH/Sync/Preview
/// metotları push dilimi geldiğinde eklenecek.
/// </summary>
public interface ISalesChannelEtsyProductAppService : IApplicationService
{
    /// <summary>Bir ÜRÜNE ait tüm Etsy kanal ürünleri (ürün-merkezli drill). Aynı kanalda birden fazla kayıt
    /// OLABİLİR; kanal set-once (değiştirilemez).</summary>
    Task<List<SalesChannelEtsyProductDto>> GetListForProductAsync(Guid productId);

    Task<SalesChannelEtsyProductDto> GetAsync(Guid id);

    Task<SalesChannelEtsyProductDto> CreateAsync(SalesChannelEtsyProductCreateDto input);

    Task<SalesChannelEtsyProductDto> UpdateAsync(Guid id, SalesChannelEtsyProductUpdateDto input);

    /// <summary>Yalnız yerel siler (Etsy'de pasifleştirme ayrı; ürün Etsy'de kalır).</summary>
    Task DeleteAsync(Guid id);

    /// <summary>Pazaryerinden İÇE AKTARMA (salt GET — Etsy'ye SIFIR yazma): kanalın Etsy mağazasındaki MEVCUT aktif
    /// listelemeler çekilir ve TAM ZİNCİR yazılır — şablon <c>Product</c> + GERÇEK offering grafı (EntityAttribute/
    /// EntityAttributeValue/EntityVariant/EntityVariantAttributeValue + ProductVariantDetail) + bağlı
    /// <c>SalesChannelEtsyProduct</c>. İdempotency: kanal kaydı = <c>EtsyListingId</c>, offering = inventory
    /// <c>product_id</c> (Sku.EtsyProductId). İkinci import dublike üretmez, mevcudu tazeler (ekleme-only). Sonuç raporu
    /// ekranda gösterilir (Trendyol import raporu deseni).</summary>
    Task<EtsyImportResultDto> ImportFromMarketplaceAsync(Guid salesChannelId);

    /// <summary>Kanalın Etsy mağazasındaki kargo profillerini (<c>getShopShippingProfiles</c>, salt GET) push
    /// kargo-profili picker'ı için döner. Shop-scoped uç → geçerli access token (<c>IEtsyTokenProvider</c>, gerekirse
    /// yenilenmiş) + <c>x-api-key</c>. Kanal/mağaza çözülmezse dostane hata. Silinmiş profiller elenir.</summary>
    Task<List<EtsyShippingProfileDto>> GetShippingProfilesAsync(Guid salesChannelId);

    /// <summary>Kanalın Etsy mağazasındaki iade politikalarını (<c>getShopReturnPolicies</c>, salt GET) iade politikası
    /// picker'ı için döner. Shop-scoped uç → geçerli access token (<c>IEtsyTokenProvider</c>, gerekirse yenilenmiş) +
    /// <c>x-api-key</c>. Kanal/mağaza çözülmezse dostane hata. Etsy politikasının başlığı olmadığından görüntü etiketi
    /// iade/değişim + süre alanlarından lokalize türetilir.</summary>
    Task<List<EtsyReturnPolicyDto>> GetReturnPoliciesAsync(Guid salesChannelId);

    /// <summary>Kanalın Etsy mağazasındaki dükkân bölümlerini (<c>getShopSections</c>, salt GET) dükkân bölümü picker'ı
    /// için döner. Shop-scoped uç → geçerli access token (<c>IEtsyTokenProvider</c>, gerekirse yenilenmiş) +
    /// <c>x-api-key</c>. Kanal/mağaza çözülmezse dostane hata.</summary>
    Task<List<EtsyShopSectionDto>> GetShopSectionsAsync(Guid salesChannelId);

    /// <summary>Kanalın Etsy mağazasında YENİ dükkân bölümü OLUŞTURUR (<c>createShopSection</c>, Etsy'ye YAZMA) — yalnız
    /// kullanıcı picker yanındaki ekle butonuyla formu doldurup kaydedince çağrılır. Oluşan bölümü (id + title) döner.</summary>
    Task<EtsyShopSectionDto> CreateShopSectionAsync(Guid salesChannelId, EtsyShopSectionInputDto input);

    /// <summary>Kanalın Etsy mağazasındaki dükkân bölümünün başlığını GÜNCELLER (<c>updateShopSection</c>, Etsy'ye YAZMA).
    /// Güncel bölümü döner.</summary>
    Task<EtsyShopSectionDto> UpdateShopSectionAsync(Guid salesChannelId, long shopSectionId, EtsyShopSectionInputDto input);

    /// <summary>Kanalın Etsy mağazasında YENİ iade politikası OLUŞTURUR (<c>createShopReturnPolicy</c>, Etsy'ye YAZMA).
    /// Oluşan politikayı (id + türetilmiş etiket) döner.</summary>
    Task<EtsyReturnPolicyDto> CreateReturnPolicyAsync(Guid salesChannelId, EtsyReturnPolicyInputDto input);

    /// <summary>Kanalın Etsy mağazasındaki iade politikasını GÜNCELLER (<c>updateShopReturnPolicy</c>, Etsy'ye YAZMA).
    /// Güncel politikayı döner.</summary>
    Task<EtsyReturnPolicyDto> UpdateReturnPolicyAsync(Guid salesChannelId, long returnPolicyId, EtsyReturnPolicyInputDto input);
}
