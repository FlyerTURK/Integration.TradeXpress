using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Pazaryeri (Etsy/Trendyol/…) importunda uzak görsel URL'lerini blob'a İNDİREN ortak boru hattı (DRY): her URL
/// için GET → guard/thumbnail çekirdeği (<see cref="ImageUploadPipeline.UploadToFolderAsync"/>) → ürün-geneli
/// blob klasörü ("Products/{ÜrünKodu}") altında ilk boş <c>GORSEL{n}</c> adına kaydeder → <see cref="ProductImage"/>
/// (Upload kaynağı). İndirme ya da guard BAŞARISIZSA o tek görsel için URL-kaynağına DÜŞER + warning loglar (import
/// DURMAZ; mevcut import dayanıklılık deseni — tek bozuk görsel tüm importu öldürmesin).
/// </summary>
public sealed class MarketplaceImageDownloader : ITransientDependency
{
    private const string ErrorCodePrefix = "TradeXpress:Product";

    // Import seyrek/toplu bir işlemdir; paylaşılan tek HttpClient yeterli (soket tükenmesi yok — önerilen yeniden
    // kullanım deseni; PublicImageLinkProvider / EtsyPingCredentialVerifier ile aynı yaklaşım). Makul timeout.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly IBlobContainer<ProductImagesContainer> _container;
    private readonly ILogger<MarketplaceImageDownloader> _logger;

    public MarketplaceImageDownloader(
        IBlobContainer<ProductImagesContainer> container,
        ILogger<MarketplaceImageDownloader> logger)
    {
        _container = container;
        _logger = logger;
    }

    /// <summary>Uzak görsel URL'lerini şablon görsellerine çevirir: her URL indirilip blob'a yazılır (Upload kaynağı,
    /// ilk görsel default). İndirme/guard başarısızsa o görsel URL-kaynağına düşer (import kırılmaz). Duplike/aşırı-uzun
    /// URL elenir; yalnız ilk <see cref="ProductConsts.MaxImageCount"/> URL işlenir (gereksiz indirme yapılmaz).
    /// <paramref name="productCode"/> boşsa blob path'i boş segment üretmesin diye HİÇ indirilmez — tüm görseller
    /// URL-kaynaklı döner.</summary>
    public async Task<List<ProductImage>> BuildFromUrlsAsync(
        string productCode, IReadOnlyList<string> imageUrls, CancellationToken cancellationToken = default)
    {
        var urls = imageUrls
            .Where(u => !string.IsNullOrWhiteSpace(u) && u.Length <= ProductConsts.ImageUrlMaxLength)
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ProductConsts.MaxImageCount)
            .ToList();
        if (urls.Count == 0)
        {
            return new List<ProductImage>();
        }

        // Ürün kodu yoksa "Products/{Kod}" boş segmente düşer → blob path'i bozulmasın diye indirmeyi atla.
        var folder = string.IsNullOrWhiteSpace(productCode)
            ? null
            : ProductImageBlobPath.Folder(productCode, null);

        var images = new List<ProductImage>(urls.Count);
        for (var i = 0; i < urls.Count; i++)
        {
            var url = urls[i];
            var isDefault = i == 0;
            var downloaded = folder is null
                ? null
                : await TryDownloadToBlobAsync(folder, url, i, isDefault, cancellationToken);

            // İndirme/guard başarısız (ya da kod yok) → URL-kaynağına düş (variantId/variantCode import'ta null).
            images.Add(downloaded
                ?? new ProductImage(ProductImageSourceType.Url, url, null, null, i, isDefault, null, null));
        }

        return images;
    }

    /// <summary>Tek URL'yi indirir + blob'a yazar → Upload kaynaklı <see cref="ProductImage"/>. Herhangi bir hatada
    /// (ağ/timeout/guard/bozuk görsel) null döner + warning loglar — çağıran URL-kaynağına düşer, import devam eder.</summary>
    private async Task<ProductImage?> TryDownloadToBlobAsync(
        string folder, string url, int index, bool isDefault, CancellationToken cancellationToken)
    {
        try
        {
            var content = await HttpClient.GetByteArrayAsync(url, cancellationToken);
            var fileName = BuildFileName(url, index);
            var uploaded = await ImageUploadPipeline.UploadToFolderAsync(
                _container, folder, fileName, content, ProductConsts.MaxImageSizeBytes, ErrorCodePrefix);
            return new ProductImage(
                ProductImageSourceType.Upload, null, uploaded.BlobName, fileName, index, isDefault, null, null);
        }
        catch (Exception ex)
        {
            // Tek bozuk/erişilemez görsel TÜM importu düşürmesin — URL-kaynağına düş, uyarı server-log'a (Blazor Server).
            _logger.LogWarning(ex, "Pazaryeri görseli indirilemedi, URL-kaynağına düşülüyor: {ImageUrl}", url);
            return null;
        }
    }

    /// <summary>Dosya adı = URL'nin son path segmenti (uzantı korunur; guard whitelist'i uzantıya bakar). Segment yoksa
    /// ya da uzantısızsa "image-{n}.jpg". Aşırı uzun ad <see cref="ProductConsts.ImageFileNameMaxLength"/> kolonuna
    /// sığmadığından güvenli ada ("image-{n}{ext}") düşülür.</summary>
    private static string BuildFileName(string url, int index)
    {
        var segment = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : Path.GetFileName(url);

        var extension = Path.GetExtension(segment).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            return $"image-{index + 1}.jpg";
        }

        if (segment.Length > ProductConsts.ImageFileNameMaxLength)
        {
            return $"image-{index + 1}{extension}";
        }

        return segment;
    }
}
