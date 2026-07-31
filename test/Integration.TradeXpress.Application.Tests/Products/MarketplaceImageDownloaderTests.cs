using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Integration.TradeXpress.Products;

/// <summary>
/// <see cref="MarketplaceImageDownloader"/> SAF unit testleri (DB'siz/DI'sız — NSubstitute).
///
/// <para>Pazaryeri import boru hattının sözleşmesi: URL seti → DAM import (OrdinalIgnoreCase dedup +
/// <see cref="ProductConsts.MaxImageCount"/> kırpması) → ürünün "Product" bağlamına replace-all link seti
/// (İLK BAŞARILI görsel kapak). Tek bozuk görsel importu ÖLDÜRMEZ (atlanır + warning); hiç görsel inmezse
/// mevcut link seti KORUNUR (<c>ReplaceForAsync</c> hiç çağrılmaz — ürün görselsiz bırakılmaz).</para>
/// </summary>
public class MarketplaceImageDownloaderTests
{
    private readonly IMediaAppService _media;
    private readonly IEntityMediaAppService _entityMedia;
    private readonly MarketplaceImageDownloader _downloader;

    public MarketplaceImageDownloaderTests()
    {
        _media = Substitute.For<IMediaAppService>();
        _entityMedia = Substitute.For<IEntityMediaAppService>();

        // Log içeriği doğrulanmıyor (warning yalnız teşhis) — NullLogger yeterli, substitute'a gerek yok.
        _downloader = new MarketplaceImageDownloader(
            _media,
            _entityMedia,
            NullLogger<MarketplaceImageDownloader>.Instance);
    }

    [Fact]
    public async Task Basari_yolunda_link_seti_replace_edilir_ve_ilk_basarili_gorsel_kapak_olur()
    {
        // İlk URL indirilemiyor (ağ hatası) → atlanır; kapak İLK BAŞARILI görsele (url2) kayar.
        var product = CreateProduct();
        const string url1 = "https://cdn.example.com/gorsel-1.jpg";
        const string url2 = "https://cdn.example.com/gorsel-2.jpg";
        const string url3 = "https://cdn.example.com/gorsel-3.jpg";
        var media2 = new MediaDto { Id = Guid.NewGuid() };
        var media3 = new MediaDto { Id = Guid.NewGuid() };

        _media.ImportFromUrlAsync(Arg.Is<MediaImportUrlDto>(d => d.Url == url1))
            .ThrowsAsync(new InvalidOperationException("ağ hatası (test)"));
        _media.ImportFromUrlAsync(Arg.Is<MediaImportUrlDto>(d => d.Url == url2))
            .Returns(media2);
        _media.ImportFromUrlAsync(Arg.Is<MediaImportUrlDto>(d => d.Url == url3))
            .Returns(media3);

        // ReplaceForAsync'e giden link setini yakala (When..Do received-sayacını KİRLETMEZ).
        List<EntityMediaLinkEditDto>? capturedLinks = null;
        _entityMedia
            .When(x => x.ReplaceForAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<List<EntityMediaLinkEditDto>>()))
            .Do(call =>
            {
                capturedLinks = call.Arg<List<EntityMediaLinkEditDto>>();
            });

        var importedCount = await _downloader.ImportToProductAsync(product, new[] { url1, url2, url3 });

        importedCount.ShouldBe(2);
        await _entityMedia.Received(1).ReplaceForAsync(
            MediaEntityNames.Product,
            product.Id,
            product.CompanyId,
            Arg.Any<List<EntityMediaLinkEditDto>>());

        var links = capturedLinks.ShouldNotBeNull();
        links.Count.ShouldBe(2);

        // Kapak: ilk BAŞARILI görsel (url2'nin medyası) — IsDefault + DisplayOrder=0.
        links[0].MediaId.ShouldBe(media2.Id);
        links[0].IsDefault.ShouldBeTrue();
        links[0].DisplayOrder.ShouldBe(0);

        // İkinci başarılı görsel kapak DEĞİL, sırası 1.
        links[1].MediaId.ShouldBe(media3.Id);
        links[1].IsDefault.ShouldBeFalse();
        links[1].DisplayOrder.ShouldBe(1);
    }

    [Fact]
    public async Task Ayni_urlnin_buyuk_harfli_kopyasi_iki_kez_import_edilmez()
    {
        // Dedup OrdinalIgnoreCase — aynı URL'in büyük harfli kopyası İKİNCİ import tetiklemez.
        var product = CreateProduct();
        const string url = "https://cdn.example.com/gorsel-a.jpg";
        var uppercaseCopy = url.ToUpperInvariant();

        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>())
            .Returns(new MediaDto { Id = Guid.NewGuid() });

        var importedCount = await _downloader.ImportToProductAsync(product, new[] { url, uppercaseCopy });

        importedCount.ShouldBe(1);
        await _media.Received(1).ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>());
    }

    [Fact]
    public async Task Tum_indirmeler_basarisizsa_mevcut_link_seti_korunur()
    {
        // Hiç görsel inmediyse ReplaceForAsync ÇAĞRILMAZ — üründeki mevcut link seti silinmez (sahte boşaltma yok).
        var product = CreateProduct();
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>())
            .ThrowsAsync(new InvalidOperationException("indirme hatası (test)"));

        var importedCount = await _downloader.ImportToProductAsync(
            product,
            new[] { "https://cdn.example.com/bozuk-1.jpg", "https://cdn.example.com/bozuk-2.jpg" });

        importedCount.ShouldBe(0);
        await _entityMedia.DidNotReceive().ReplaceForAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<List<EntityMediaLinkEditDto>>());
    }

    [Fact]
    public async Task MaxImageCount_uzeri_urller_hic_indirilmez()
    {
        // Kırpma İNDİRMEDEN ÖNCE uygulanır: fazla URL'ler için ağ/import maliyeti hiç oluşmaz.
        var product = CreateProduct();
        var urls = Enumerable.Range(1, ProductConsts.MaxImageCount + 3)
            .Select(i => $"https://cdn.example.com/gorsel-{i}.jpg")
            .ToList();

        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>())
            .Returns(new MediaDto { Id = Guid.NewGuid() });

        var importedCount = await _downloader.ImportToProductAsync(product, urls);

        importedCount.ShouldBe(ProductConsts.MaxImageCount);
        await _media.Received(ProductConsts.MaxImageCount).ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>());
    }

    private static Product CreateProduct()
    {
        // Saf unit test — repository yok; Id ABP tarafından atanmadığından Guid.Empty kalır,
        // downloader sözleşmesi product.Id'yi olduğu gibi geçirdiğinden assertion'lar etkilenmez.
        return new Product(Guid.NewGuid(), "IMGTEST", "Görsel İndirme Test Ürünü");
    }
}
