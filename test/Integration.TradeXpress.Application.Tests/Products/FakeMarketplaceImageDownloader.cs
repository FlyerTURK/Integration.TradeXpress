using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Integration.TradeXpress.Attachments;
using Microsoft.Extensions.Logging;

namespace Integration.TradeXpress.Products;

/// <summary>
/// Testlerde ağa çıkmayan indirici — YALNIZ indirme adımı sahtelenir; bağlama akışının tamamı (mevcut bağları
/// koruma, dedup, cover kuralı, sınır kırpması, hedef bağlam seçimi) GERÇEK koddan geçer.
///
/// <para><b>Neden bu seviyede sahtelenir:</b> gerçek <c>MediaAppService.ImportFromUrlAsync</c> HTTP'ye çıkar;
/// testte her URL sessizce başarısız oluyordu ve içe aktarımın görsel dalı hiç koşmuyordu — yani zincirin
/// yarısı test edilmemiş durumdaydı. <c>IHttpClientFactory</c>'yi sahtelemek de olurdu ama o yol blob yazımı +
/// ImageSharp thumbnail üretimini de devreye sokar (yavaş ve konu dışı).</para>
///
/// <para><b>Bilinçli sınır:</b> KÜTÜPHANE kaydı (Media satırı) AÇILMAZ, yalnız kimliği üretilir. Bu yüzden
/// üretilen bağlar <c>EntityMediaAppService.GetForAsync</c>'in "yetim link" elemesine takılır ve okuma
/// yollarında görünmez; testler bağları <c>EntityMediaLink</c> tablosundan DOĞRUDAN sayar. Media satırı da
/// açsaydık gerçek blob içeriği (thumbnail üretimi) gerekirdi.</para>
///
/// <para><b>Kimlik URL'den DETERMİNİSTİK türetilir</b> — aynı URL daima aynı <c>MediaId</c>'yi verir, yani
/// üretimdeki ContentHash dedup'ı taklit edilir: ikinci içe aktarım aynı görseli ikinci kez BAĞLAMAZ.</para>
/// </summary>
public class FakeMarketplaceImageDownloader : MarketplaceImageDownloader
{
    public FakeMarketplaceImageDownloader(
        IMediaAppService media,
        IEntityMediaAppService entityMedia,
        ILogger<MarketplaceImageDownloader> logger)
        : base(media, entityMedia, logger)
    {
    }

    protected override Task<MediaDto?> TryImportAsync(string url, string fileName)
    {
        return Task.FromResult<MediaDto?>(new MediaDto
        {
            Id = BuildDeterministicId(url),
            FileName = fileName,
            MediaType = MediaType.Image,
        });
    }

    /// <summary>URL → sabit Guid (içerik-hash dedup'ının test karşılığı). MD5 kriptografik amaçla DEĞİL, yalnız
    /// 16 baytlık kararlı bir kimlik üretmek için kullanılır.</summary>
    private static Guid BuildDeterministicId(string url)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(url.ToLowerInvariant()));
        return new Guid(hash);
    }
}
