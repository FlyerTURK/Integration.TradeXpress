using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

/* Inherit from this class for your domain layer tests. */
public abstract class TradeXpressDomainTestBase<TStartupModule> : TradeXpressTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
