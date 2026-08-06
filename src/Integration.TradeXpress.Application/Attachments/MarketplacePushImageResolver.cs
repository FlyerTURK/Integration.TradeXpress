using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.Variants;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Pazaryerine GİDECEK ürün görsellerini tek yerden çözer (N11 + Trendyol ortak; Etsy bayt yüklediği için kullanmaz).
///
/// <para><b>Neden merkezi:</b> sıra/limit/tür kuralları her kanalda AYNI ve sessizce bozulabilir cinsten
/// (kapak kayarsa pazaryerinde vitrin görseli değişir, video sızarsa XML reddedilir). Kanal başına kopyalanan
/// döngüler zamanla birbirinden ayrılırdı.</para>
///
/// <para><b>Kaynak:</b> YALNIZ merkezi DAM (K2 kararı). Legacy <c>ProductImage</c> geri düşüşü 2026-07-31'de
/// kaldırıldı: varyant-özel medya artık "ProductVariant" bağlamında yaşıyor, kayıt geneli ise "Product"
/// bağlamında — ikisi de DAM'da. Görsel çözümü tek kaynaktan gelir.</para>
/// </summary>
public class MarketplacePushImageResolver : ITransientDependency
{
    // Agnostik varyant tablosunda ürün varyantlarının sahip-adı (ProductAppService.ProductEntityName ile aynı dize).
    private const string ProductVariantOwnerEntityName = "Product";

    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaPublicLinkProvider _mediaPublicLink;
    private readonly IRepository<EntityVariant, Guid> _variantRepository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<MarketplacePushImageResolver> _logger;

    public MarketplacePushImageResolver(
        IEntityMediaAppService entityMedia,
        IMediaPublicLinkProvider mediaPublicLink,
        IRepository<EntityVariant, Guid> variantRepository,
        IAsyncQueryableExecuter asyncExecuter,
        ICurrentTenant currentTenant,
        ILogger<MarketplacePushImageResolver> logger)
    {
        _entityMedia = entityMedia;
        _mediaPublicLink = mediaPublicLink;
        _variantRepository = variantRepository;
        _asyncExecuter = asyncExecuter;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    /// <summary>ÜRÜN-DÜZEYİ push görselleri — varyant görselini AYRICA taşıyamayan kanal modeli için
    /// (bugünkü N11/Trendyol ürün görsel API'leri): ürünün seti + TÜM varyant setleri BİRLEŞTİRİLİR
    /// (2026-08-01 Hakan kararı: "varyant desteklemeyen sitelerde varyant fotoğrafları ana ürün
    /// fotoğraflarına eklensin"). Sıra: ürün seti (kapak önde) → ana varyant → diğer varyantlar (kod sırası);
    /// aynı medya iki bağlamda da linkliyse BİR kez gider. En fazla <paramref name="maxCount"/> adet.
    /// Adresi üretilemeyen görsel SESSİZCE atlanır (2026-07-28 kararı: push durmasın), ama loglanır.</summary>
    public virtual async Task<List<string>> ResolveAsync(Product product, int maxCount)
    {
        var media = await _entityMedia.GetPushMediaAsync(MediaEntityNames.Product, product.Id, MediaType.Image);
        media = await AppendVariantMediaAsync(product, media);

        var urls = BuildFromMedia(product, media);

        // Pazaryeri ürün başına sınırlı görsel kabul eder. Sınır DAM'da YOK (kütüphane sınırsız), bu yüzden
        // burada uygulanır — kapak-önce sıralamadan SONRA, yani kırpılan hep en arkadaki görsellerdir.
        return urls.Count > maxCount ? urls.Take(maxCount).ToList() : urls;
    }

    /// <summary>
    /// Push'a GİDECEK görsellerin DAM kimlikleri — <see cref="ResolveAsync(Product,int)"/> ile AYNI küme ve
    /// AYNI sıra (kapak önde). Push GEÇMİŞİ için: delil kaydı "hangi görsel gitti" sorusuna URL ile değil
    /// kimlikle cevap vermeli — imzalı adresin ömrü kısadır, kimlik kalıcıdır.
    ///
    /// <para><b>Adresi üretilemeyen görsel BURADA DA ATLANIR</b> — push'a gitmeyen bir görseli geçmişe
    /// yazmak, gönderilmemiş bir şeyi gönderilmiş göstermek olurdu.</para>
    /// </summary>
    public virtual async Task<List<Guid>> ResolveMediaIdsAsync(Product product, int maxCount)
    {
        var media = await _entityMedia.GetPushMediaAsync(MediaEntityNames.Product, product.Id, MediaType.Image);
        media = await AppendVariantMediaAsync(product, media);

        var ids = media
            .Where(item => _mediaPublicLink.TryCreateLink(item.MediaId, _currentTenant.Id) is not null)
            .Select(item => item.MediaId)
            .ToList();

        return ids.Count > maxCount ? ids.Take(maxCount).ToList() : ids;
    }

    /// <summary>SKU-DÜZEYİ push görselleri — varyant görselini destekleyen kanal modeli için (Faz-2 push
    /// hedefi; 2026-08-01 Hakan kararı: "varyantı destekleyen sistemse ana ürün + varyant fotoğrafları").
    /// Varyantın KENDİ seti; hiç fotoğrafı yoksa ürünün kayıt geneli seti (SKU görselsiz kalmasın).
    /// Kardeş varyanta DÜŞÜLMEZ — SKU modelinde başka varyantın fotoğrafı yanlış ürünü gösterir.</summary>
    public virtual async Task<List<string>> ResolveAsync(Product product, Guid? variantId, int maxCount)
    {
        var media = new List<PushMediaDto>();
        if (variantId is not null && variantId != Guid.Empty)
        {
            media = await _entityMedia.GetPushMediaAsync(MediaEntityNames.ProductVariant, variantId.Value, MediaType.Image);
        }

        if (media.Count == 0)
        {
            media = await _entityMedia.GetPushMediaAsync(MediaEntityNames.Product, product.Id, MediaType.Image);
        }

        var urls = BuildFromMedia(product, media);
        return urls.Count > maxCount ? urls.Take(maxCount).ToList() : urls;
    }

    // Aktif varyantların setleri (ana önce, sonra kod sırası) ürün setinin ARKASINA eklenir; MediaId dedup —
    // aynı görsel hem üründe hem varyantta linkliyse pazaryerine bir kez gider. Kapak semantiği bozulmaz:
    // ürün setinin kapağı listenin başında kalır; ürün seti boşsa ilk varyantın kapağı öne geçer.
    private async Task<List<PushMediaDto>> AppendVariantMediaAsync(Product product, List<PushMediaDto> productMedia)
    {
        var merged = new List<PushMediaDto>(productMedia);
        var seen = new HashSet<Guid>(productMedia.Select(m => m.MediaId));

        var variants = await _asyncExecuter.ToListAsync(
            (await _variantRepository.GetQueryableAsync())
                .Where(v => v.EntityName == ProductVariantOwnerEntityName && v.EntityId == product.Id && v.IsActive)
                .OrderByDescending(v => v.IsMain)
                .ThenBy(v => v.Code));

        foreach (var variant in variants)
        {
            var variantMedia = await _entityMedia.GetPushMediaAsync(MediaEntityNames.ProductVariant, variant.Id, MediaType.Image);
            foreach (var item in variantMedia)
            {
                if (seen.Add(item.MediaId))
                {
                    merged.Add(item);
                }
            }
        }

        return merged;
    }

    private List<string> BuildFromMedia(Product product, List<PushMediaDto> media)
    {
        var urls = new List<string>();
        var skipped = 0;

        foreach (var item in media)
        {
            // Pazaryeri sunucusu oturum taşıyamaz → imzalı süreli dış adres. Tenant jetonun içinde (uç doğru
            // bağlamı açsın diye); anahtar/taban adres yapılandırılmamışsa sağlayıcı null döner.
            var link = _mediaPublicLink.TryCreateLink(item.MediaId, _currentTenant.Id);
            if (link is not null)
            {
                urls.Add(link);
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            _logger.LogWarning(
                "Push görseli atlandı: {SkippedCount}/{TotalCount} medya için dış bağlantı üretilemedi (ürün {ProductId}). "
                + "MediaPublicLink:SigningKey ve MediaPublicLink:BaseUrl yapılandırılmamış olabilir.",
                skipped, media.Count, product.Id);
        }

        return urls;
    }

}
