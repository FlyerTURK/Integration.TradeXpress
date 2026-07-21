using System;
using System.Collections.Generic;

namespace Integration.TradeXpress.Channels;

/// <summary>
/// Bir kanalın kurulum (provisioning) SONUÇ RAPORU — adım-adım durum listesi (sessiz geçilmez). Panel bu raporu
/// canlı gösterir ("Yeniden Kur" ile idempotent tekrarlanır). <see cref="AllReady"/> = hiçbir adım Failed değil
/// (Skipped adımlar ön-koşul bekler ama kanalı "başarısız" saymaz — ör. OAuth henüz yapılmadı).
/// </summary>
public class ProvisioningResultDto
{
    /// <summary>Kurulumu yapılan satış kanalının kimliği.</summary>
    public Guid ChannelId { get; set; }

    /// <summary>Yürütülen adımların sonuçları (yürütme sırasında).</summary>
    public List<ProvisioningStepResultDto> Steps { get; set; } = new();

    /// <summary>Kanal her yönüyle hazır mı — hiçbir adım <see cref="ProvisioningStatus.Failed"/> değilse true
    /// (Skipped adımlar bekleyen ön-koşuldur, başarısızlık değildir).</summary>
    public bool AllReady
    {
        get
        {
            return Steps.TrueForAll(step => step.Status != ProvisioningStatus.Failed);
        }
    }
}
