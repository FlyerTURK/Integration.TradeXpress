using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Pazaryeri (Etsy/Trendyol/…) importunda uzak görselleri merkezi DAM'a İNDİREN ortak boru hattı (DRY):
/// her URL <see cref="IMediaAppService.ImportFromUrlAsync"/> ile kütüphaneye alınır (self-contained blob;
/// ContentHash dedup — aynı görsel ikinci kez İNDİRİLMEZ, mevcut medyaya link'lenir) ve ürünün "Product"
/// bağlamına link seti olarak yazılır (ilk görsel kapak).
///
/// <para><b>Dayanıklılık:</b> indirme/guard BAŞARISIZSA o görsel ATLANIR + warning loglanır (import DURMAZ —
/// tek bozuk görsel tüm importu öldürmesin). Legacy'deki "URL-kaynağına düş" davranışı bilinçli KALKTI:
/// DAM'da içerik daima blob'dadır (URL saklanmaz) ve push yalnız DAM'dan okur — indirilemeyen görselin URL'ini
/// taşımak onu hiçbir yüzeyde görünmez kılardı (sahte başarı).</para>
/// </summary>
public sealed class MarketplaceImageDownloader : ITransientDependency
{
    private readonly IMediaAppService _media;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly ILogger<MarketplaceImageDownloader> _logger;

    public MarketplaceImageDownloader(
        IMediaAppService media,
        IEntityMediaAppService entityMedia,
        ILogger<MarketplaceImageDownloader> logger)
    {
        _media = media;
        _entityMedia = entityMedia;
        _logger = logger;
    }

    /// <summary>Uzak görsel URL'lerini ürünün DAM link setine çevirir: her URL kütüphaneye import edilir
    /// (dedup), ürünün "Product" bağlamındaki link seti BAŞTAN yazılır (replace-all; ilk başarılı görsel kapak).
    /// Duplike URL elenir; yalnız ilk <see cref="ProductConsts.MaxImageCount"/> URL işlenir.
    /// Dönen değer: başarıyla import edilen görsel sayısı (0 = ürün görselsiz kaldı; çağıran loglayabilir).</summary>
    public async Task<int> ImportToProductAsync(
        Product product, IReadOnlyList<string> imageUrls)
    {
        var urls = imageUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ProductConsts.MaxImageCount)
            .ToList();
        if (urls.Count == 0)
        {
            return 0;
        }

        var links = new List<EntityMediaLinkEditDto>(urls.Count);
        foreach (var url in urls)
        {
            var media = await TryImportAsync(url);
            if (media is null)
            {
                continue;
            }

            links.Add(new EntityMediaLinkEditDto
            {
                MediaId = media.Id,
                DisplayOrder = links.Count,
                IsDefault = links.Count == 0,
                IsActive = true,
            });
        }

        if (links.Count > 0)
        {
            await _entityMedia.ReplaceForAsync(MediaEntityNames.Product, product.Id, product.CompanyId, links);
        }

        return links.Count;
    }

    /// <summary>Tek URL'yi kütüphaneye import eder. Herhangi bir hatada (ağ/timeout/SSRF guard/bozuk içerik)
    /// null döner + warning loglar — çağıran görseli atlar, import devam eder.</summary>
    private async Task<MediaDto?> TryImportAsync(string url)
    {
        try
        {
            return await _media.ImportFromUrlAsync(new MediaImportUrlDto { Url = url });
        }
        catch (Exception ex)
        {
            // Tek bozuk/erişilemez görsel TÜM importu düşürmesin — atla, uyarı server-log'a (Blazor Server).
            _logger.LogWarning(ex, "Pazaryeri görseli DAM'a import edilemedi, atlanıyor: {ImageUrl}", url);
            return null;
        }
    }
}
