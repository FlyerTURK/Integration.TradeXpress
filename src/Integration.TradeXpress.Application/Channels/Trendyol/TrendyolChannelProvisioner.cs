using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Channels;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.SalesChannels;
using Integration.TradeXpress.SalesChannels.Trendyol;
using Integration.TradeXpress.Trendyol;
using Integration.TradeXpress.TrendyolCategories;
using Integration.TradeXpress.TrendyolProducts;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Integration.TradeXpress.Channels.Trendyol;

/// <summary>
/// Trendyol kanalı kurulum sağlayıcısı — bir Trendyol kanalı kaydedilince kullanıma hazır gelmesi için gereken
/// ilk-kurulum adımlarını sırayla, resilient (her adım bağımsız) yürütür. Adımlar:
/// <list type="number">
///   <item><b>ping</b> — kimlik doğrulama testi (<see cref="ITrendyolCredentialVerifier.VerifyOrThrowAsync"/>,
///   hafif authenticated GET). Kimlik geçersizse adım Failed'a düşer (gerçek hata sürfaslanır).</item>
///   <item><b>categories</b> — host-global kategori ağacı (<see cref="ITrendyolCategoryAppService.SyncCategoriesAsync"/>).</item>
///   <item><b>import</b> — pazaryerindeki mevcut satıcı ürünlerini içe aktar
///   (<see cref="ISalesChannelTrTrendyolProductAppService.ImportFromMarketplaceAsync"/>, salt GET, idempotent).</item>
/// </list>
/// <para><b>Kimlik ön-koşulu (Etsy shop-scoped deseninin Trendyol karşılığı):</b> Trendyol kimliği PER-COMPANY kanal
/// kaydından çözülür (SellerId/ApiKey/ApiSecret). Çözülemezse (şirket/kanal yok) üç adım da dostane Skipped ("kimlik
/// eksik"), throw YOK. Kimlik ÇÖZÜLDÜĞÜNDE ping onu gerçekten sınar; geçersizse ping Failed olur ama sonraki adımlar
/// yine bağımsız denenir (resilient — Etsy'nin bağımsız-adım felsefesi).</para>
/// </summary>
public class TrendyolChannelProvisioner : ChannelProvisionerBase
{
    private readonly ITrendyolCredentialResolver _credentialResolver;
    private readonly ITrendyolCredentialVerifier _credentialVerifier;
    private readonly ITrendyolCategoryAppService _categoryAppService;
    private readonly ISalesChannelTrTrendyolProductAppService _productAppService;

    public TrendyolChannelProvisioner(
        IStringLocalizer<TradeXpressResource> localizer,
        ILogger<TrendyolChannelProvisioner> logger,
        ITrendyolCredentialResolver credentialResolver,
        ITrendyolCredentialVerifier credentialVerifier,
        ITrendyolCategoryAppService categoryAppService,
        ISalesChannelTrTrendyolProductAppService productAppService)
        : base(localizer, logger)
    {
        _credentialResolver = credentialResolver;
        _credentialVerifier = credentialVerifier;
        _categoryAppService = categoryAppService;
        _productAppService = productAppService;
    }

    public override SalesChannelType ChannelType
    {
        get
        {
            return SalesChannelType.TrTrendyol;
        }
    }

    public override async Task<ProvisioningResultDto> ProvisionAsync(Guid channelId, CancellationToken cancellationToken)
    {
        // Ön-koşul bir kez çözülür (per-company kimlik). Çözülemezse tüm adımlar dostane atlanır (Etsy shopReady deseni).
        var credentials = await TryResolveCredentialsAsync();

        var steps = new List<ProvisioningStepResultDto>
        {
            await RunPingStepAsync(credentials, cancellationToken),
            await RunCategoriesStepAsync(credentials),
            await RunImportStepAsync(channelId, credentials),
        };

        return BuildResult(channelId, steps);
    }

    /// <summary>Kimlik doğrulama adımı — kimlik yoksa Skipped; varsa doğrulayıcı (hafif authenticated GET) çalışır,
    /// geçerliyse Success, geçersiz/erişilemezse doğrulayıcı throw eder → RunStepAsync Failed'a çevirir.</summary>
    private async Task<ProvisioningStepResultDto> RunPingStepAsync(TrendyolCredentials? credentials, CancellationToken cancellationToken)
    {
        return await RunStepAsync(
            "ping",
            L["ChannelProvisioning:Trendyol:Step:Ping"],
            async () =>
            {
                if (credentials is null)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Trendyol:CredentialsMissing"]);
                }

                await _credentialVerifier.VerifyOrThrowAsync(
                    credentials.SellerId, credentials.ApiKey, credentials.ApiSecret, cancellationToken);
                return StepOutcome.Success(L["ChannelProvisioning:Trendyol:Ping:Ok"]);
            });
    }

    /// <summary>Kategori ağacı adımı — host-global; kimlik yoksa Skipped, varsa senkronize edilen kategori sayısı Success.</summary>
    private async Task<ProvisioningStepResultDto> RunCategoriesStepAsync(TrendyolCredentials? credentials)
    {
        return await RunStepAsync(
            "categories",
            L["ChannelProvisioning:Trendyol:Step:Categories"],
            async () =>
            {
                if (credentials is null)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Trendyol:CredentialsMissing"]);
                }

                var changed = await _categoryAppService.SyncCategoriesAsync();
                return StepOutcome.Success(L["ChannelProvisioning:Trendyol:Categories:Synced", changed]);
            });
    }

    /// <summary>Ürün içe aktarma adımı — kimlik yoksa Skipped, varsa işlenen kanal kaydı (yeni+güncellenen) sayısı Success.</summary>
    private async Task<ProvisioningStepResultDto> RunImportStepAsync(Guid channelId, TrendyolCredentials? credentials)
    {
        return await RunStepAsync(
            "import",
            L["ChannelProvisioning:Trendyol:Step:Import"],
            async () =>
            {
                if (credentials is null)
                {
                    return StepOutcome.Skipped(L["ChannelProvisioning:Trendyol:CredentialsMissing"]);
                }

                var result = await _productAppService.ImportFromMarketplaceAsync(channelId);
                var importedCount = result.CreatedChannelProducts + result.UpdatedChannelProducts;
                return StepOutcome.Success(L["ChannelProvisioning:Trendyol:Import:Done", importedCount]);
            });
    }

    /// <summary>Per-company Trendyol kimliğini çözer; şirket/kanal yoksa çözücü <c>BusinessException</c> fırlatır →
    /// null döner (adımlar dostane atlanır). Etsy'nin <c>IsShopReachableAsync</c> ön-koşul-çözme deseninin karşılığı.</summary>
    private async Task<TrendyolCredentials?> TryResolveCredentialsAsync()
    {
        try
        {
            return await _credentialResolver.ResolveForCurrentCompanyAsync();
        }
        catch (BusinessException)
        {
            return null;
        }
    }
}
