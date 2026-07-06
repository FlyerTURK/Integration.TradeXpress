using Integration.TradeXpress.SalesChannels.N11;
using Shouldly;
using Volo.Abp.Modularity;
using Xunit;

namespace Integration.TradeXpress.SalesChannels;

/// <summary>
/// DI çözümleme ağı — satış kanalı app service'leri konteynerden KURULABİLMELİ. Bir bağımlılık (ör.
/// <see cref="IN11CredentialVerifier"/>) ExposeServices/registration ile doğru bildirilmezse ctor aktivasyonu
/// Autofac'te patlar → N11 edit formu ekranda sessizce açılmaz. Bu test o regresyonu KIRMIZI yapar.
/// </summary>
public abstract class SalesChannelAppServiceResolutionTests<TStartupModule> : TradeXpressApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    [Fact]
    public void Sales_channel_services_should_resolve_from_container()
    {
        GetRequiredService<ISalesChannelAppService>().ShouldNotBeNull();
        GetRequiredService<ISalesChannelTrN11AppService>().ShouldNotBeNull();
        GetRequiredService<ISalesChannelTrTrendyolAppService>().ShouldNotBeNull();
        GetRequiredService<IN11CredentialVerifier>().ShouldNotBeNull();
    }
}
