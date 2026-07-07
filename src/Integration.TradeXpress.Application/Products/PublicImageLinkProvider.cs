using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Yüklenmiş (blob) ürün görseli için DIŞARIDAN erişilebilir GEÇİCİ link üretir — marketplace push'u linki verir,
/// pazaryeri görseli KENDİ sistemine import eder (2026-07-07 kullanıcı kararı; anonim endpoint YOK). Sağlayıcı
/// yapılandırılmamışsa (None) null döner → blob görseller push'a girmez (mevcut güvenli davranış).
/// </summary>
public interface IPublicImageLinkProvider
{
    /// <summary>Blob için geçici public link — sağlayıcı yoksa null; sağlayıcı hatasında dostane BusinessException
    /// (push eksik görselli listeleme oluşturmasın diye DURUR).</summary>
    Task<string?> TryCreateTemporaryLinkAsync(string blobName, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IPublicImageLinkProvider"/> — config <c>PublicImageLink:Provider</c> ile seçilir:
/// <c>None</c> (varsayılan; link üretilmez) ya da <c>ImgBb</c> (api.imgbb.com'a base64 upload; <c>expiration</c>
/// saniyesi sonunda otomatik silinir — pazaryeri importu için dakikalar yeter). ImgBb wire formatı CANLI
/// DOĞRULANMAMIŞTIR (test: gerçek API key ile push — bkz. appsettings PublicImageLink bölümü).
/// </summary>
public sealed class PublicImageLinkProvider : IPublicImageLinkProvider, ITransientDependency
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    private readonly IConfiguration _configuration;
    private readonly IBlobContainer<ProductImagesContainer> _container;

    public PublicImageLinkProvider(IConfiguration configuration, IBlobContainer<ProductImagesContainer> container)
    {
        _configuration = configuration;
        _container = container;
    }

    public async Task<string?> TryCreateTemporaryLinkAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var provider = _configuration["PublicImageLink:Provider"];
        if (!string.Equals(provider, "ImgBb", StringComparison.OrdinalIgnoreCase))
        {
            return null;   // None/boş → blob görseller push'a girmez (güvenli varsayılan)
        }

        var apiKey = _configuration["PublicImageLink:ImgBb:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BusinessException("TradeXpress:Product:ImageLinkFailed")
                .WithData("Reason", "ImgBb ApiKey missing");
        }

        var content = await _container.GetAllBytesOrNullAsync(blobName);
        if (content is null)
        {
            throw new BusinessException("TradeXpress:Product:ImageLinkFailed")
                .WithData("Reason", $"blob not found: {blobName}");
        }

        return await UploadToImgBbAsync(apiKey, content, cancellationToken);
    }

    /// <summary>imgbb upload: POST /1/upload?key=&amp;expiration= (form: image=base64) → data.url.</summary>
    private async Task<string> UploadToImgBbAsync(string apiKey, byte[] content, CancellationToken cancellationToken)
    {
        var expiration = _configuration["PublicImageLink:ImgBb:ExpirationSeconds"] ?? "600";
        var url = $"https://api.imgbb.com/1/upload?key={Uri.EscapeDataString(apiKey)}&expiration={Uri.EscapeDataString(expiration)}";

        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["image"] = Convert.ToBase64String(content),
        });
        using var response = await HttpClient.PostAsync(url, body, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new BusinessException("TradeXpress:Product:ImageLinkFailed")
                .WithData("Reason", $"HTTP {(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("url", out var link)
            && link.GetString() is { Length: > 0 } value)
        {
            return value;
        }

        throw new BusinessException("TradeXpress:Product:ImageLinkFailed")
            .WithData("Reason", "unexpected imgbb response");
    }
}
