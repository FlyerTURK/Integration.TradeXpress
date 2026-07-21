using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Localization;
using Integration.TradeXpress.SalesChannels;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Channels;

/// <summary>
/// Kanal kurulum sağlayıcıları için ORTAK taban — resilient adım yürütücüsü (<see cref="RunStepAsync"/>) + sonuç
/// toplayıcı (<see cref="BuildResult"/>) burada; her pazaryeri sağlayıcısı yalnız kendi adımlarını yazar (DRY).
/// Adım yürütücüsü her adımı try/catch ile sarar: eylem <see cref="StepOutcome"/> (Success/Skipped) döner ya da
/// FIRLATIR → fırlatan Failed'a çevrilir + sunucu loguna yazılır; <b>bir adımın hatası diğerlerini ÖLDÜRMEZ</b>
/// (görsel-indirme/worker fallback deseni). <see cref="ITransientDependency"/> markerı sayesinde ABP her somut
/// alt-sınıfı <see cref="IChannelProvisioner"/> olarak kaydeder → dispatcher hepsini enjekte eder.
/// </summary>
public abstract class ChannelProvisionerBase : IChannelProvisioner, ITransientDependency
{
    protected ChannelProvisionerBase(IStringLocalizer<TradeXpressResource> localizer, ILogger logger)
    {
        L = localizer;
        Logger = logger;
    }

    /// <summary>Lokalizasyon — adım başlıkları/mesajları kullanıcı kültüründe (Blazor Server: circuit kültürü).</summary>
    protected IStringLocalizer<TradeXpressResource> L { get; }

    protected ILogger Logger { get; }

    /// <inheritdoc/>
    public abstract SalesChannelType ChannelType { get; }

    /// <inheritdoc/>
    public abstract Task<ProvisioningResultDto> ProvisionAsync(Guid channelId, CancellationToken cancellationToken);

    /// <summary>Tek bir adımı resilient yürütür: <paramref name="action"/> Success/Skipped döner → aynen raporlanır;
    /// FIRLATIRSA Failed'a çevrilir (dostane lokalize mesaj + tam istisna sunucu loguna). Adım ASLA dışarı throw etmez
    /// → çağıran döngü kesintisiz sonraki adıma geçer.</summary>
    protected async Task<ProvisioningStepResultDto> RunStepAsync(string stepKey, string title, Func<Task<StepOutcome>> action)
    {
        try
        {
            var outcome = await action();
            return new ProvisioningStepResultDto(stepKey, title, outcome.Status, outcome.Message);
        }
        catch (Exception ex)
        {
            // Teknik detay sunucu logunda; kullanıcıya dostane genel mesaj (in-process BusinessException kodu sızmasın).
            Logger.LogError(ex, "Kanal kurulum adımı '{StepKey}' hata verdi.", stepKey);
            return new ProvisioningStepResultDto(stepKey, title, ProvisioningStatus.Failed, L["ChannelProvisioning:StepFailed"]);
        }
    }

    /// <summary>Biriken adım sonuçlarını rapora sarar (<see cref="ProvisioningResultDto.AllReady"/> = hiç Failed yok).</summary>
    protected static ProvisioningResultDto BuildResult(Guid channelId, List<ProvisioningStepResultDto> steps)
    {
        return new ProvisioningResultDto
        {
            ChannelId = channelId,
            Steps = steps,
        };
    }
}
