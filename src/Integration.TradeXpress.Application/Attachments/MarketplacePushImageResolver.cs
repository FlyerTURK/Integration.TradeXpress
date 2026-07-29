using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Products;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.Attachments;

/// <summary>
/// Pazaryerine GİDECEK ürün görsellerini tek yerden çözer (N11 + Trendyol ortak; Etsy bayt yüklediği için kullanmaz).
///
/// <para><b>Neden merkezi:</b> sıra/limit/tür kuralları her kanalda AYNI ve sessizce bozulabilir cinsten
/// (kapak kayarsa pazaryerinde vitrin görseli değişir, video sızarsa XML reddedilir). Kanal başına kopyalanan
/// döngüler zamanla birbirinden ayrılırdı.</para>
///
/// <para><b>Kaynak:</b> merkezi DAM (K2 kararı). Göç (Faz 2) tamamlanana kadar DAM'da medyası OLMAYAN ürün için
/// legacy <see cref="ProductImage"/> setine düşülür — aksi halde göçten önceki her push görselsiz kalırdı.
/// Faz 5'te bu geri düşüş ve <see cref="IPublicImageLinkProvider"/> birlikte kalkar.</para>
/// </summary>
public class MarketplacePushImageResolver : ITransientDependency
{
    private readonly IEntityMediaAppService _entityMedia;
    private readonly IMediaPublicLinkProvider _mediaPublicLink;
    private readonly IPublicImageLinkProvider _legacyImageLink;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<MarketplacePushImageResolver> _logger;

    public MarketplacePushImageResolver(
        IEntityMediaAppService entityMedia,
        IMediaPublicLinkProvider mediaPublicLink,
        IPublicImageLinkProvider legacyImageLink,
        ICurrentTenant currentTenant,
        ILogger<MarketplacePushImageResolver> logger)
    {
        _entityMedia = entityMedia;
        _mediaPublicLink = mediaPublicLink;
        _legacyImageLink = legacyImageLink;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    /// <summary>Ürünün push görsel adresleri — kapak önce, en fazla <paramref name="maxCount"/> adet.
    /// Adresi üretilemeyen görsel SESSİZCE atlanır (2026-07-28 Hakan kararı: push durmasın), ama loglanır:
    /// sessiz eksilme aksi halde "ürünün zaten 3 görseli var" gibi görünürdü.</summary>
    public virtual async Task<List<string>> ResolveAsync(Product product, int maxCount)
    {
        var media = await _entityMedia.GetPushMediaAsync(MediaEntityNames.Product, product.Id, MediaType.Image);

        var urls = media.Count > 0
            ? BuildFromMedia(product, media)
            : await BuildFromLegacyAsync(product);

        // Pazaryeri ürün başına sınırlı görsel kabul eder. Sınır DAM'da YOK (kütüphane sınırsız), bu yüzden
        // burada uygulanır — kapak-önce sıralamadan SONRA, yani kırpılan hep en arkadaki görsellerdir.
        return urls.Count > maxCount ? urls.Take(maxCount).ToList() : urls;
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

    /// <summary>Göç öncesi geri düşüş — legacy ürün görselleri. URL kaynaklılar doğrudan, yüklenmişler ImgBb
    /// sağlayıcısı üzerinden (yapılandırılmamışsa atlanır; bugünkü davranışın aynısı).</summary>
    private async Task<List<string>> BuildFromLegacyAsync(Product product)
    {
        var urls = new List<string>();

        foreach (var image in product.Images.OrderByDescending(i => i.IsDefault).ThenBy(i => i.DisplayOrder))
        {
            if (image.SourceType == ProductImageSourceType.Url && !string.IsNullOrWhiteSpace(image.Url))
            {
                urls.Add(image.Url!);
            }
            else if (image.SourceType == ProductImageSourceType.Upload && !string.IsNullOrEmpty(image.BlobName))
            {
                var link = await _legacyImageLink.TryCreateTemporaryLinkAsync(image.BlobName!);
                if (link is not null)
                {
                    urls.Add(link);
                }
            }
        }

        return urls;
    }
}
