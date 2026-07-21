namespace Integration.TradeXpress.Channels;

/// <summary>
/// Tek bir kurulum adımının sonucu — kararlı kimlik (<paramref name="StepKey"/>), LOKALİZE görüntü başlığı/mesajı
/// ve durum rozeti. Başlık/mesaj sunucuda (kullanıcı kültüründe) üretilir; panel yalnız gösterir + duruma göre renk
/// verir. <paramref name="Message"/> null olabilir (başarılı sade adımda gerekmez).
/// </summary>
/// <param name="StepKey">Kararlı adım anahtarı (ör. "taxonomy", "shipping-profiles", "import") — UI keying + log.</param>
/// <param name="Title">Kullanıcıya gösterilen lokalize adım başlığı.</param>
/// <param name="Status">Adımın sonuç durumu.</param>
/// <param name="Message">Lokalize açıklama (ör. "N profil bulundu", "OAuth gerekli") — opsiyonel.</param>
public sealed record ProvisioningStepResultDto(
    string StepKey,
    string Title,
    ProvisioningStatus Status,
    string? Message);
