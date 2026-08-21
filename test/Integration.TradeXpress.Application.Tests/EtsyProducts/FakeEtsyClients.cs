using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Orders;
using Volo.Abp;

namespace Integration.TradeXpress.EtsyProducts;

/// <summary>
/// Etsy listeleme istemcisinin TEST sahtesi — testte ağ yok (READ-ONLY pazaryeri ilkesi testte de korunur; Trendyol/N11
/// sahtelerinin Etsy karşılığı). <see cref="RemoteListings"/>'e konan listelemeler içe aktarıma olduğu gibi servis
/// edilir; varyasyon fotoğrafı bağları <see cref="VariationImagesByListingId"/>'den listeleme başına döner.
///
/// <para><b>İçe aktarımın kullanmadığı uçlar KIRMIZIDIR</b> (sentinel <c>TradeXpress:Test:*</c> kodu): kazayla
/// mağaza/kargo/iade ucuna uzanan bir değişiklik testte sessizce geçmesin. Sentinel kod ÜRETİM kodlarından ayrıdır —
/// aksi hâlde "kazayla sahteye ulaşıldı" ile "gerçek HTTP hatası" ayırt edilemezdi.</para>
///
/// <para><b>Varyasyon fotoğrafı ucu iki ayrı "boş"u temsil edebilir:</b> sözlükte kayıt YOKSA boş liste döner
/// (üretimdeki 404 = "bu listelemede bağ yok" karşılığı), <see cref="FailVariationImages"/> açıksa uç PATLAR
/// (dayanıklılık dalını sınayan bayrak).</para>
/// </summary>
public sealed class FakeEtsyProductClient : IEtsyProductClient
{
    /// <summary>Sahte mağaza envanteri — içe aktarımın okuyacağı aktif listelemeler.</summary>
    public List<EtsyRemoteListing> RemoteListings { get; } = new();

    /// <summary>Listeleme başına varyasyon fotoğrafı bağları. Kayıt yoksa uç BOŞ liste döner (hata değil).</summary>
    public Dictionary<long, List<EtsyVariationImage>> VariationImagesByListingId { get; } = new();

    /// <summary>Varyasyon fotoğrafı ucunu dostane bir <see cref="BusinessException"/> ile PATLATIR (uzak taraf
    /// başarısız durum döndürdü) — içe aktarımın bu yüzden DURMADIĞINI doğrulamak için.</summary>
    public bool FailVariationImages { get; set; }

    /// <summary>Varyasyon fotoğrafı ucunu TAŞIMA KATMANI hatasıyla patlatır — <c>HttpClient</c> zaman aşımının
    /// birebir şekli (<c>TaskCanceledException</c> + iç <see cref="TimeoutException"/>).
    ///
    /// <para><b>Neden ayrı bayrak:</b> gerçek hayatta en olası arıza budur ve <see cref="BusinessException"/>
    /// DEĞİLDİR. Dayanıklılık yalnız <see cref="FailVariationImages"/> ile sınandığında, üretimdeki dar bir catch
    /// (yalnız <see cref="BusinessException"/>) testten YEŞİL geçer ama canlıda tüm içe aktarımı düşürür.</para></summary>
    public bool FailVariationImagesWithTransportError { get; set; }

    /// <summary>Varyasyon fotoğrafı ucunun çağrıldığı listeleme kimlikleri (gereksiz çağrı yapılmadığını gösterir).</summary>
    public List<long> VariationImageCalls { get; } = new();

    public Task<IReadOnlyList<EtsyRemoteListing>> GetAllListingsAsync(
        EtsyCredentials credentials, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EtsyRemoteListing>>(RemoteListings.ToList());
    }

    public Task<IReadOnlyList<EtsyVariationImage>> GetVariationImagesAsync(
        EtsyCredentials credentials, long listingId, CancellationToken cancellationToken = default)
    {
        VariationImageCalls.Add(listingId);
        if (FailVariationImagesWithTransportError)
        {
            throw new TaskCanceledException(
                "TradeXpress:Test:EtsyVariationImagesTimedOut", new TimeoutException());
        }

        if (FailVariationImages)
        {
            throw new BusinessException("TradeXpress:Test:EtsyVariationImagesFailed");
        }

        if (!VariationImagesByListingId.TryGetValue(listingId, out var images))
        {
            return Task.FromResult<IReadOnlyList<EtsyVariationImage>>(new List<EtsyVariationImage>());
        }

        return Task.FromResult<IReadOnlyList<EtsyVariationImage>>(images.ToList());
    }

    public Task<IReadOnlyList<EtsyShippingProfileSummary>> GetShopShippingProfilesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyEndpointNotAllowed");
    }

    public Task<IReadOnlyList<EtsyReturnPolicySummary>> GetShopReturnPoliciesAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyEndpointNotAllowed");
    }

    public Task<IReadOnlyList<EtsyShopSectionSummary>> GetShopSectionsAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyEndpointNotAllowed");
    }

    public Task<EtsyShopSectionSummary> CreateShopSectionAsync(
        EtsyCredentials credentials, string title, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyWriteNotAllowed");
    }

    public Task<EtsyShopSectionSummary> UpdateShopSectionAsync(
        EtsyCredentials credentials, long shopSectionId, string title, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyWriteNotAllowed");
    }

    public Task<EtsyReturnPolicySummary> CreateReturnPolicyAsync(
        EtsyCredentials credentials, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyWriteNotAllowed");
    }

    public Task<EtsyReturnPolicySummary> UpdateReturnPolicyAsync(
        EtsyCredentials credentials, long returnPolicyId, bool acceptsReturns, bool acceptsExchanges, int? returnDeadlineDays,
        CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyWriteNotAllowed");
    }

    public Task<EtsyIdentity?> VerifyIdentityAsync(
        EtsyCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw new BusinessException("TradeXpress:Test:EtsyEndpointNotAllowed");
    }
}
