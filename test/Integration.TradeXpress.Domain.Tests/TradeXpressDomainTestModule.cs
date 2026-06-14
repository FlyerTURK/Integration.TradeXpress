using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

[DependsOn(
    typeof(TradeXpressDomainModule),
    typeof(TradeXpressTestBaseModule)
)]
public class TradeXpressDomainTestModule : AbpModule
{

}
