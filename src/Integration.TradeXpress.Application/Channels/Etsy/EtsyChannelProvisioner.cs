using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Channels;
using Integration.TradeXpress.EtsyProducts;
using Integration.TradeXpress.EtsyTaxonomies;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.Orders;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Timing;

namespace Integration.TradeXpress.Channels.Etsy;

/// <summary>
/// Etsy kanalı kurulum sağlayıcısı — bir Etsy kanalı kaydedilince kullanıma hazır gelmesi için gereken ilk-kurulum
/// senkronizasyonlarını sırayla, resilient (her adım bağımsız) yürütür. Adımlar:
/// <list type="number">
///   <item><b>auth</b> — kimlik doğrulama (<see cref="IEtsyProductClient.VerifyIdentityAsync"/>, <c>getMe</c> salt GET);
///   token'ın GEÇERLİ + mağazaya bağlı olduğunu teyit eder. Shop-scoped → OAuth/mağaza yoksa Skipped, varsa Success
///   ("kimlik doğrulandı, shop {id}"). EN BAŞA çalışır (diğer shop-scoped adımların ön-testi).</item>
///   <item><b>taxonomy</b> — host-global kategori ağacı (<see cref="EtsyTaxonomySyncManager.SyncIfStaleAsync"/>);
///   token GEREKMEZ (app-key). Bayat/boşsa çekilir (Success), zaten günceclse atlanır (Skipped).</item>
///   <item><b>shipping-profiles</b> — mağaza kargo profilleri reachability (<see cref="ISalesChannelEtsyProductAppService.GetShippingProfilesAsync"/>,
///   salt GET — Etsy'ye YAZMA). Shop-scoped → geçerli OAuth + çözülmüş mağaza yoksa Skipped.</item>
///   <item><b>return-policies</b> — mağaza iade politikaları reachability (<see cref="ISalesChannelEtsyProductAppService.GetReturnPoliciesAsync"/>,
///   salt GET — Etsy'ye YAZMA). Shop-scoped → OAuth yoksa Skipped, varsa politika sayısı Success.</item>
///   <item><b>import</b> — mağazadaki mevcut listelemeleri içe aktar (<see cref="ISalesChannelEtsyProductAppService.ImportFromMarketplaceAsync"/>,
///   idempotent). Shop-scoped → OAuth yoksa Skipped.</item>
/// </list>
/// <para><b>Token ön-koşulu:</b> shop-scoped adımlar için domain <see cref="SalesChannelEtsy.IsConnected"/> (geçerli
/// refresh token) + çözülmüş <c>ShopId</c> kontrol edilir — ağ turu ATMADAN (token yenileme/rotasyon şeffaf olarak
/// asıl çağrının içinde <c>IEtsyTokenProvider</c> ile olur). Karşılanmıyorsa dostane Skipped ("OAuth tamamlanınca
/// çalışacak"), throw YOK.</para>
/// </summary>
public class EtsyChannelProvisioner : ChannelProvisionerBase
{
    private readonly EtsyTaxonomySyncManager _taxonomySyncManager;
    private readonly ISalesChannelEtsyProductAppService _productAppService;
    private readonly IEtsyProductClient _productClient;
    private readonly IRepository<SalesChannelEtsy, Guid> _channelRepository;
    private readonly IClock _clock;

    public EtsyChannelProvisioner(
        IStringLocalizer<TradeXpressResource> localizer,
        ILogger<EtsyChannelProvisioner> logger,
        EtsyTaxonomySyncManager taxonomySyncManager,
        ISalesChannelEtsyProductAppService productAppService,
        IEtsyProductClient productClient,
        IRepository<SalesChannelEtsy, Guid> channelRepository,
        IClock clock)
        : base(localizer, logger)
    {
        _taxonomySyncManager = taxonomySyncManager;
        _productAppService = productAppService;
        _productClient = productClient;
        _channelRepository = channelRepository;
        _clock = clock;
    }

    public override SalesChannelType ChannelType
    {
        get
        {
            return SalesChannelType.Etsy;
        }
    }

    public override async Task<ProvisioningResultDto> ProvisionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        // Shop-scoped adımların ön-koşulu bir kez çözülür (token + çözülmüş mağaza). Kanal bulunamazsa/erişilemezse
        // (beklenmez — dispatcher doğruladı) shop-scoped adımlar güvenle atlanır. Erişilebilir kanal auth adımına
        // kimlik demeti kurmak için de kullanılır (ikinci DB turu atmadan).
        var reachableChannel = await TryGetReachableChannelAsync(channelId);

        var steps = new List<ProvisioningStepResultDto>
        {
            await RunAuthStepAsync(reachableChannel),
            await RunTaxonomyStepAsync(cancellationToken),
            await RunShippingProfilesStepAsync(channelId, reachableChannel is not null),
            await RunReturnPoliciesStepAsync(channelId, reachableChannel is not null),
            await RunImportStepAsync(channelId, reachableChannel is not null),
        };

        return BuildResult(channelId, steps);
    }

    /// <summary>Kimlik doğrulama adımı (İLK) — shop-scoped; OAuth/mağaza yoksa Skipped. Varsa <c>getMe</c> salt GET ile
    /// token'ın geçerli + mağazaya bağlı olduğu teyit edilir (Etsy'ye YAZMA yok); GET başarısızsa (geçersiz token)
    /// RunStepAsync Failed'a çevirir. Başarı mesajı çözülen mağaza kimliğini içerir.</summary>
    private async Task<ProvisioningStepResultDto> RunAuthStepAsync(SalesChannelEtsy? reachableChannel)
    {
        return await RunStepAsync(
            "auth",
            L["ChannelProvisioning:Etsy:Step:Auth"],
            async () =>
            {
                if (reachableChannel is null)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Etsy:OAuthRequired"]);
                }

                // Kimlik demeti shop-scoped dilimlerle AYNI (kanal id + x-api-key = {keystring}:{secret} + shopId);
                // token client içinde IEtsyTokenProvider ile şeffaf çözülür/yenilenir.
                var credentials = new EtsyCredentials(
                    reachableChannel.Id, $"{reachableChannel.Keystring}:{reachableChannel.SharedSecret}", reachableChannel.ShopId!);
                var identity = await _productClient.VerifyIdentityAsync(credentials);

                // getMe yanıtındaki shop_id (varsa) yoksa kanaldaki çözülmüş ShopId — GET 200 zaten geçerliliğin kanıtı.
                var shopId = identity?.ShopId?.ToString() ?? reachableChannel.ShopId;
                return StepOutcome.Success(L["ChannelProvisioning:Etsy:Auth:Verified", shopId!]);
            });
    }

    /// <summary>Kategori ağacı adımı — host-global, token gerekmez. Bayat/boşsa çekilir (Success), zaten güncelse Skipped.</summary>
    private async Task<ProvisioningStepResultDto> RunTaxonomyStepAsync(CancellationToken cancellationToken)
    {
        return await RunStepAsync(
            "taxonomy",
            L["ChannelProvisioning:Etsy:Step:Taxonomy"],
            async () =>
            {
                var interval = _taxonomySyncManager.ResolveSyncInterval();
                var synced = await _taxonomySyncManager.SyncIfStaleAsync(interval, cancellationToken);
                return synced
                    ? StepOutcome.Success(L["ChannelProvisioning:Etsy:Taxonomy:Synced"])
                    : StepOutcome.Skipped(L["ChannelProvisioning:Etsy:Taxonomy:AlreadyCurrent"]);
            });
    }

    /// <summary>Kargo profilleri reachability adımı — shop-scoped; OAuth/mağaza yoksa Skipped, varsa profil sayısı Success.</summary>
    private async Task<ProvisioningStepResultDto> RunShippingProfilesStepAsync(Guid channelId, bool shopReady)
    {
        return await RunStepAsync(
            "shipping-profiles",
            L["ChannelProvisioning:Etsy:Step:ShippingProfiles"],
            async () =>
            {
                if (!shopReady)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Etsy:OAuthRequired"]);
                }

                var profiles = await _productAppService.GetShippingProfilesAsync(channelId);
                return StepOutcome.Success(L["ChannelProvisioning:Etsy:ShippingProfiles:Found", profiles.Count]);
            });
    }

    /// <summary>İade politikaları reachability adımı — shop-scoped; OAuth/mağaza yoksa Skipped, varsa politika sayısı Success.</summary>
    private async Task<ProvisioningStepResultDto> RunReturnPoliciesStepAsync(Guid channelId, bool shopReady)
    {
        return await RunStepAsync(
            "return-policies",
            L["ChannelProvisioning:Etsy:Step:ReturnPolicies"],
            async () =>
            {
                if (!shopReady)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Etsy:OAuthRequired"]);
                }

                var policies = await _productAppService.GetReturnPoliciesAsync(channelId);
                return StepOutcome.Success(L["ChannelProvisioning:Etsy:ReturnPolicies:Found", policies.Count]);
            });
    }

    /// <summary>Ürün içe aktarma adımı — shop-scoped; OAuth/mağaza yoksa Skipped, varsa işlenen kanal kaydı sayısı Success.</summary>
    private async Task<ProvisioningStepResultDto> RunImportStepAsync(Guid channelId, bool shopReady)
    {
        return await RunStepAsync(
            "import",
            L["ChannelProvisioning:Etsy:Step:Import"],
            async () =>
            {
                if (!shopReady)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Etsy:OAuthRequired"]);
                }

                var result = await _productAppService.ImportFromMarketplaceAsync(channelId);
                var importedCount = result.CreatedChannelProducts + result.UpdatedChannelProducts;
                return StepOutcome.Success(L["ChannelProvisioning:Etsy:Import:Done", importedCount]);
            });
    }

    /// <summary>Shop-scoped adımların ön-koşulu: kanal Etsy'ye BAĞLI (geçerli refresh token) VE mağaza çözülmüş
    /// (<c>ShopId</c> dolu) ise kanalı döner, aksi halde null (adımlar dostane atlanır). Ağ turu atmaz (token yenileme
    /// asıl çağrı içinde şeffaf); yalnız domain durumu okunur. Dönen kanal auth adımının kimlik demetini de besler.</summary>
    private async Task<SalesChannelEtsy?> TryGetReachableChannelAsync(Guid channelId)
    {
        var channel = await _channelRepository.FindAsync(channelId);
        if (channel is null)
        {
            return null;
        }

        return channel.IsConnected(_clock.Now.ToUniversalTime()) && !string.IsNullOrWhiteSpace(channel.ShopId)
            ? channel
            : null;
    }
}
