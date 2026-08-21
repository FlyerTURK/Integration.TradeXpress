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
/// <para>Pazaryeri import boru hattının sözleşmesi: URL seti → DAM import (OrdinalIgnoreCase dedup) → hedefin
/// medya bağlamına EKLEMELİ link seti. Tek bozuk görsel importu ÖLDÜRMEZ (atlanır + warning); hiç yeni görsel
/// eklenmiyorsa <c>ReplaceForAsync</c> hiç çağrılmaz (mevcut set korunur).</para>
///
/// <para><b>Buradaki çivilerin asıl konusu (2026-08-20):</b> varyant görselleri hiç yazılmıyordu (asıl canlı
/// eksik), ve yazan tek yol replace-all olduğu için varyant bağlamı HER tur yazılmaya başlayınca kullanıcının
/// elle bağladığı görseli koparacaktı. Testler dört şeyi çiviler: mevcut bağ KORUNUR · aynı medya İKİ kez
/// bağlanmaz · mevcut COVER korunur · sınır taşmasında YENİLER kırpılır (mevcutlar değil).</para>
///
/// <para><b>Kapsam notu:</b> kayıt-geneli ("Product") bağlam bugün yalnız ürün KURULURKEN yazılıyor, yani orada
/// korunacak bir kullanıcı bağı henüz oluşamıyor. Aşağıdaki çiviler bu yüzden "yaşanmış hatayı" değil
/// SÖZLEŞMEYİ tutar: <c>MarketplaceImageDownloader</c> iki bağlamda ortaktır ve varyant bağlamında aynı davranış fiilen her tur koşar.</para>
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

        // Varsayılan: hedefte HİÇ bağ yok (List<T> için NSubstitute otomatik değer üretmez — null dönerdi).
        _entityMedia.GetForAsync(Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(_ => new List<EntityMediaLinkEditDto>());

        // Log içeriği doğrulanmıyor (warning yalnız teşhis) — NullLogger yeterli, substitute'a gerek yok.
        _downloader = new MarketplaceImageDownloader(
            _media,
            _entityMedia,
            NullLogger<MarketplaceImageDownloader>.Instance);
    }

    [Fact]
    public async Task Basari_yolunda_link_seti_yazilir_ve_ilk_basarili_gorsel_kapak_olur()
    {
        // İlk URL indirilemiyor (ağ hatası) → atlanır; cover İLK BAŞARILI görsele (url2) kayar.
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

        var capture = CaptureReplacedLinks();

        var result = await _downloader.ImportToProductAsync(product, new[] { url1, url2, url3 });

        result.ImportedCount.ShouldBe(2);
        result.SkippedForCapacityCount.ShouldBe(0);
        await _entityMedia.Received(1).ReplaceForAsync(
            MediaEntityNames.Product,
            product.Id,
            product.CompanyId,
            Arg.Any<List<EntityMediaLinkEditDto>>());

        var links = capture.Links.ShouldNotBeNull();
        links.Count.ShouldBe(2);

        // Cover: ilk BAŞARILI görsel (url2'nin medyası) — IsDefault + DisplayOrder=0.
        links[0].MediaId.ShouldBe(media2.Id);
        links[0].IsDefault.ShouldBeTrue();
        links[0].DisplayOrder.ShouldBe(0);

        // İkinci başarılı görsel cover DEĞİL, sırası 1.
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

        StubDistinctMedia();

        var result = await _downloader.ImportToProductAsync(product, new[] { url, uppercaseCopy });

        result.ImportedCount.ShouldBe(1);
        await _media.Received(1).ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>());
    }

    [Fact]
    public async Task Tum_indirmeler_basarisizsa_mevcut_link_seti_korunur()
    {
        // Hiç görsel inmediyse ReplaceForAsync ÇAĞRILMAZ — hedefteki mevcut link seti silinmez (sahte boşaltma yok).
        var product = CreateProduct();
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>())
            .ThrowsAsync(new InvalidOperationException("indirme hatası (test)"));

        var result = await _downloader.ImportToProductAsync(
            product,
            new[] { "https://cdn.example.com/bozuk-1.jpg", "https://cdn.example.com/bozuk-2.jpg" });

        result.ImportedCount.ShouldBe(0);
        await _entityMedia.DidNotReceive().ReplaceForAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<List<EntityMediaLinkEditDto>>());
    }

    // ── ① Kullanıcı bağı KORUNUR, pazaryeri görseli ÜSTÜNE eklenir ──────────────────────────────────

    /// <summary>Sözleşme: hedefte kullanıcının kütüphaneden elle bağladığı görsel varsa İÇE AKTARIM ONU KOPARMAZ,
    /// pazaryeri görselini sonuna ekler. Replace-all yazan bir indiricide bağ giderdi ve kayıp SESSİZ olurdu (dosya
    /// kütüphanede kalır, yalnız BAĞ gider) — ancak galeri boşaldığında fark edilirdi. Bugün bu senaryo fiilen
    /// VARYANT bağlamında yaşanır (her tur yazılır); kayıt-geneli bağlam yalnız kuruluşta yazıldığı için orada
    /// aynı davranış ileriye dönük emniyettir.</summary>
    [Fact]
    public async Task Kullanicinin_bagladigi_gorsel_korunur_pazaryeri_gorseli_ustune_eklenir()
    {
        var product = CreateProduct();
        var userMediaId = Guid.NewGuid();
        var marketplaceMedia = new MediaDto { Id = Guid.NewGuid() };

        _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id)
            .Returns(_ => new List<EntityMediaLinkEditDto>
            {
                new() { MediaId = userMediaId, DisplayOrder = 0, IsDefault = true, IsActive = true },
            });
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>()).Returns(marketplaceMedia);

        var capture = CaptureReplacedLinks();

        var result = await _downloader.ImportToProductAsync(
            product, new[] { "https://cdn.example.com/pazaryeri.jpg" });

        result.ImportedCount.ShouldBe(1);

        var links = capture.Links.ShouldNotBeNull();
        links.Count.ShouldBe(2);
        links[0].MediaId.ShouldBe(userMediaId);          // kullanıcının bağı DURUYOR ve sırası korundu
        links[1].MediaId.ShouldBe(marketplaceMedia.Id);  // pazaryeri görseli SONA eklendi
    }

    // ── ② Aynı medya İKİ kez bağlanmaz (idempotent tekrar-import) ───────────────────────────────────

    /// <summary>İçerik dedup'ı ContentHash'ledir: aynı görselin ikinci içe aktarımı AYNI <c>MediaId</c>'yi
    /// döndürür. Bağ ikinci kez açılmamalı — açılsaydı her import galeriye aynı fotoğrafın bir kopyasını daha
    /// ekler ve sınır birkaç turda dolardı.</summary>
    [Fact]
    public async Task Zaten_bagli_medya_ikinci_kez_baglanmaz()
    {
        var product = CreateProduct();
        var mediaId = Guid.NewGuid();

        _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id)
            .Returns(_ => new List<EntityMediaLinkEditDto>
            {
                new() { MediaId = mediaId, DisplayOrder = 0, IsDefault = true, IsActive = true },
            });
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>()).Returns(new MediaDto { Id = mediaId });

        var result = await _downloader.ImportToProductAsync(
            product, new[] { "https://cdn.example.com/ayni-gorsel.jpg" });

        result.ImportedCount.ShouldBe(0);
        result.AlreadyLinkedCount.ShouldBe(1);

        // Değişiklik YOK → gereksiz sil+yaz turu da YOK.
        await _entityMedia.DidNotReceive().ReplaceForAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<List<EntityMediaLinkEditDto>>());
    }

    // ── ③ Mevcut COVER korunur ──────────────────────────────────────────────────────────────────────

    /// <summary>Cover (<c>EntityMediaLink.IsDefault</c>) kullanıcının VİTRİN seçimidir (push'ta ilk görsel odur).
    /// Pazaryerinden gelen görselin cover'ı devralması, kullanıcının kararını hiçbir uyarı olmadan geri almak olurdu.</summary>
    [Fact]
    public async Task Mevcut_kapak_pazaryeri_gorseline_devredilmez()
    {
        var product = CreateProduct();
        var userMediaId = Guid.NewGuid();

        _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id)
            .Returns(_ => new List<EntityMediaLinkEditDto>
            {
                new() { MediaId = Guid.NewGuid(), DisplayOrder = 0, IsDefault = false, IsActive = true },
                new() { MediaId = userMediaId, DisplayOrder = 1, IsDefault = true, IsActive = true },
            });
        StubDistinctMedia();

        var capture = CaptureReplacedLinks();

        await _downloader.ImportToProductAsync(product, new[] { "https://cdn.example.com/yeni.jpg" });

        var links = capture.Links.ShouldNotBeNull();
        links.Count.ShouldBe(3);
        links.Count(l => l.IsDefault).ShouldBe(1);
        links.Single(l => l.IsDefault).MediaId.ShouldBe(userMediaId);   // cover KULLANICININ seçtiği görselde kaldı
    }

    // ── ④ Sınır taşmasında YENİLER kırpılır, mevcutlar durur ────────────────────────────────────────

    /// <summary>Sınır BİRLEŞİK listeye uygulanır ve kırpma YENİ gelenlere düşer: kullanıcının görselini
    /// pazaryeri görseline yer açmak için silmek, kullanıcı emeğini yok etmek olurdu. Kırpma sessiz değildir —
    /// dönüş değerinde sayılır (ve warning loglanır).</summary>
    [Fact]
    public async Task Sinir_dolduysa_yeni_gorseller_kirpilir_mevcutlar_durur()
    {
        var product = CreateProduct();
        var existing = Enumerable.Range(0, ProductConsts.MaxImageCount)
            .Select(i => new EntityMediaLinkEditDto
            {
                MediaId = Guid.NewGuid(),
                DisplayOrder = i,
                IsDefault = i == 0,
                IsActive = true,
            })
            .ToList();

        _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id).Returns(_ => existing.ToList());
        StubDistinctMedia();

        var result = await _downloader.ImportToProductAsync(
            product,
            new[] { "https://cdn.example.com/fazla-1.jpg", "https://cdn.example.com/fazla-2.jpg" });

        result.ImportedCount.ShouldBe(0);
        result.SkippedForCapacityCount.ShouldBe(2);

        // Kapasite dolu → İNDİRME BİLE yapılmaz (boşa ağ/blob maliyeti) ve mevcut set'e DOKUNULMAZ.
        await _media.DidNotReceive().ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>());
        await _entityMedia.DidNotReceive().ReplaceForAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<List<EntityMediaLinkEditDto>>());
    }

    /// <summary>Kısmî kapasite: sınıra kalan yer kadarı bağlanır, kalanı kırpılır ve SAYILIR.</summary>
    [Fact]
    public async Task Kalan_kapasite_kadari_baglanir_gerisi_kirpilir()
    {
        var product = CreateProduct();
        var existingCount = ProductConsts.MaxImageCount - 1;
        var existing = Enumerable.Range(0, existingCount)
            .Select(i => new EntityMediaLinkEditDto
            {
                MediaId = Guid.NewGuid(),
                DisplayOrder = i,
                IsDefault = i == 0,
                IsActive = true,
            })
            .ToList();

        _entityMedia.GetForAsync(MediaEntityNames.Product, product.Id).Returns(_ => existing.ToList());
        StubDistinctMedia();

        var capture = CaptureReplacedLinks();

        var result = await _downloader.ImportToProductAsync(
            product,
            new[] { "https://cdn.example.com/yeni-1.jpg", "https://cdn.example.com/yeni-2.jpg", "https://cdn.example.com/yeni-3.jpg" });

        result.ImportedCount.ShouldBe(1);
        result.SkippedForCapacityCount.ShouldBe(2);
        await _media.Received(1).ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>());

        var links = capture.Links.ShouldNotBeNull();
        links.Count.ShouldBe(ProductConsts.MaxImageCount);
        links.Select(l => l.DisplayOrder).ShouldBe(Enumerable.Range(0, ProductConsts.MaxImageCount));
    }

    // ── ⑤ Varyant bağlamı ───────────────────────────────────────────────────────────────────────────

    /// <summary>Varyanta özel görsel KENDİ bağlamına yazılır ("ProductVariant" + varyant Id'si) — kayıt geneli
    /// bağlamla karışmaz (CLAUDE.md §6). Kütüphane adı ürün + varyant kodundan türetilir ki hangi fotoğrafın
    /// hangi varyanta ait olduğu kütüphanede ADINDAN okunsun.</summary>
    [Fact]
    public async Task Varyant_metodu_ProductVariant_baglamina_yazar()
    {
        var variantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var media = new MediaDto { Id = Guid.NewGuid() };
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>()).Returns(media);

        var capture = CaptureReplacedLinks();

        var result = await _downloader.ImportToVariantAsync(
            variantId, companyId, "PRD", "KIRMIZI", new[] { "https://cdn.example.com/kirmizi.png" });

        result.ImportedCount.ShouldBe(1);

        // Hedef: ürün değil VARYANT bağlamı.
        await _entityMedia.Received(1).ReplaceForAsync(
            MediaEntityNames.ProductVariant,
            variantId,
            companyId,
            Arg.Any<List<EntityMediaLinkEditDto>>());
        await _entityMedia.DidNotReceive().ReplaceForAsync(
            MediaEntityNames.Product,
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<List<EntityMediaLinkEditDto>>());

        // Kütüphane adı: "{ÜrünKodu}-{VaryantKodu}-{sıra}{uzantı}" (uzantı URL'den korunur).
        await _media.Received(1).ImportFromUrlAsync(
            Arg.Is<MediaImportUrlDto>(d => d.FileName == "PRD-KIRMIZI-1.png"));

        var links = capture.Links.ShouldNotBeNull();
        links.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();   // hiç bağ yoktu → ilk gelen cover
    }

    /// <summary>Varyant bağlamında da EKLEMELİ davranış geçerlidir — ürün bağlamıyla ORTAK indiriciden geldiği için
    /// iki bağlam zamanla ayrışamaz.</summary>
    [Fact]
    public async Task Varyanta_elle_baglanan_gorsel_de_korunur()
    {
        var variantId = Guid.NewGuid();
        var userMediaId = Guid.NewGuid();
        _entityMedia.GetForAsync(MediaEntityNames.ProductVariant, variantId)
            .Returns(_ => new List<EntityMediaLinkEditDto>
            {
                new() { MediaId = userMediaId, DisplayOrder = 0, IsDefault = true, IsActive = true },
            });
        StubDistinctMedia();

        var capture = CaptureReplacedLinks();

        await _downloader.ImportToVariantAsync(
            variantId, Guid.NewGuid(), "PRD", "MAVI", new[] { "https://cdn.example.com/mavi.jpg" });

        var links = capture.Links.ShouldNotBeNull();
        links.Count.ShouldBe(2);
        links[0].MediaId.ShouldBe(userMediaId);
        links[0].IsDefault.ShouldBeTrue();
    }

    // ── Yardımcılar ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Her çağrıda AYRI medya döndürür — tek örneği <c>Returns</c>'e vermek tüm indirmeleri aynı
    /// <c>MediaId</c>'ye düşürür ve dedup dalı testin konusu olmadığı yerde bile devreye girerdi.</summary>
    private void StubDistinctMedia()
    {
        _media.ImportFromUrlAsync(Arg.Any<MediaImportUrlDto>())
            .Returns(_ => new MediaDto { Id = Guid.NewGuid() });
    }

    /// <summary><c>ReplaceForAsync</c>'e giden link setini yakalar (When..Do received-sayacını KİRLETMEZ).</summary>
    private LinkCapture CaptureReplacedLinks()
    {
        var capture = new LinkCapture();
        _entityMedia
            .When(x => x.ReplaceForAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<Guid?>(),
                Arg.Any<List<EntityMediaLinkEditDto>>()))
            .Do(call =>
            {
                capture.Links = call.Arg<List<EntityMediaLinkEditDto>>();
            });
        return capture;
    }

    private sealed class LinkCapture
    {
        public List<EntityMediaLinkEditDto>? Links { get; set; }
    }

    private static Product CreateProduct()
    {
        // Saf unit test — repository yok; Id ABP tarafından atanmadığından Guid.Empty kalır,
        // downloader sözleşmesi product.Id'yi olduğu gibi geçirdiğinden assertion'lar etkilenmez.
        return new Product(Guid.NewGuid(), "IMGTEST", "Görsel İndirme Test Ürünü");
    }
}
