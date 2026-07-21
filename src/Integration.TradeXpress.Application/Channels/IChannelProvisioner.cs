using System;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.SalesChannels;

namespace Integration.TradeXpress.Channels;

/// <summary>
/// Kanal-TİPİ başına kurulum sağlayıcısı (Application-içi sözleşme) — Etsy/N11/Trendyol farklı adım kümeleri.
/// Dispatcher (<see cref="ChannelProvisioningAppService"/>) <c>IEnumerable&lt;IChannelProvisioner&gt;</c>'ı enjekte
/// eder ve <see cref="ChannelType"/>'a göre uygun olanı seçer. Uygulayan sınıf <see cref="ChannelProvisionerBase"/>'ten
/// türer (resilient adım yürütücüsü + sonuç toplayıcı orada; yeni pazaryeri yalnız adımlarını yazar).
///
/// <para><b>Ajan 2 için (N11/Trendyol):</b> <see cref="ChannelProvisionerBase"/>'ten türet, <see cref="ChannelType"/>'ı
/// ver, <c>ProvisionAsync</c> içinde her adımı <c>RunStepAsync(stepKey, title, action)</c> ile sar; action
/// <c>StepOutcome.Success(...)</c> / <c>StepOutcome.Skipped(...)</c> döner ya da fırlatır (fırlatan Failed'a dönüşür).
/// Sonuçları <c>BuildResult(channelId, steps)</c> ile topla. Mevcut worker'ları (N11ReferenceSyncWorker vb.) adım
/// olarak çağırabilirsin.</para>
/// </summary>
public interface IChannelProvisioner
{
    /// <summary>Bu sağlayıcının ele aldığı kanal tipi — dispatcher tipe göre seçer.</summary>
    SalesChannelType ChannelType { get; }

    /// <summary>Kanal için tüm ilk-kurulum adımlarını resilient + idempotent yürütür; adım-adım sonuç döner
    /// (hiçbir adım throw etmez — hatalar rapora Failed olarak yansır).</summary>
    Task<ProvisioningResultDto> ProvisionAsync(Guid channelId, CancellationToken cancellationToken);
}
