using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Channels;

/// <summary>
/// Kanal kurulum ORKESTRATÖRÜ (kanal-nötr yüzey) — bir satış kanalı için o tipe özgü TÜM ilk-kurulum
/// senkronizasyonlarını resilient + idempotent yürütür ve adım-adım sonuç raporunu döner. UI "Kurulum" paneli
/// bunu çağırır (create-success'te otomatik; sonra elle "Yeniden Kur"). Kanal tipine göre uygun
/// <c>IChannelProvisioner</c>'a delege eder; provisioner yoksa dostane "desteklenmiyor" adımı döner.
/// </summary>
public interface IChannelProvisioningAppService : IApplicationService
{
    /// <summary>Kanalı bulur → tipini çözer → uygun provisioner'a delege eder → adım-adım sonuç raporunu döner.
    /// Adım hataları YUTULUR (rapora yansır), tek çağrı ATOMİK değildir (her adım bağımsız/idempotent).</summary>
    Task<ProvisioningResultDto> ProvisionAsync(Guid salesChannelId);
}
