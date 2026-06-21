using Volo.Abp.Localization;

namespace Integration.Framework.Localization;

/// <summary>
/// Framework katmanının lokalizasyon resource'u — generic alan validasyonu mesajları
/// (PropertyRequired/TooShort/TooLong/OutOfRange) burada yaşar; <c>Integration.Framework:*</c>
/// error-code'ları buraya map'lenir (bkz. <c>IntegrationFrameworkDomainSharedModule</c>).
/// </summary>
[LocalizationResourceName("IntegrationFramework")]
public class IntegrationFrameworkResource
{
}
