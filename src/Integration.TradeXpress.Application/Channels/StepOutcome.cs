namespace Integration.TradeXpress.Channels;

/// <summary>
/// Bir kurulum adımının BAŞARILI/ATLANDI çıktısı (durum + lokalize mesaj). Adım eylemi bunu döner;
/// <see cref="ChannelProvisionerBase.RunStepAsync"/> bunu <see cref="ProvisioningStepResultDto"/>'ya sarar. Adım
/// FIRLARSA yürütücü onu <see cref="ProvisioningStatus.Failed"/>'a çevirir → adım eylemi Failed'ı kendi DÖNMEZ,
/// yalnız Success/Skipped döner (fail-fast semantiği yürütücüde merkezîdir).
/// </summary>
/// <param name="Status">Adımın sonuç durumu (Success ya da Skipped).</param>
/// <param name="Message">Lokalize açıklama (opsiyonel).</param>
public sealed record StepOutcome(ProvisioningStatus Status, string? Message)
{
    /// <summary>Adım başarıyla tamamlandı — opsiyonel lokalize özet mesajı ("N profil bulundu" gibi).</summary>
    public static StepOutcome Success(string? message = null)
    {
        return new StepOutcome(ProvisioningStatus.Success, message);
    }

    /// <summary>Ön-koşul karşılanmadı → adım atlandı — lokalize sebep ("OAuth gerekli" gibi).</summary>
    public static StepOutcome Skipped(string? message = null)
    {
        return new StepOutcome(ProvisioningStatus.Skipped, message);
    }
}
