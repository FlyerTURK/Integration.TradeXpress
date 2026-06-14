using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

public abstract class TradeXpressApplicationTestBase<TStartupModule> : TradeXpressTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
