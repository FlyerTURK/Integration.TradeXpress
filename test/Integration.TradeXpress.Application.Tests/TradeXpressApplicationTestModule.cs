using Volo.Abp.Modularity;

namespace Integration.TradeXpress;

[DependsOn(
    typeof(TradeXpressApplicationModule),
    typeof(TradeXpressDomainTestModule)
)]
public class TradeXpressApplicationTestModule : AbpModule
{

}
