using Integration.TradeXpress.Orchestration;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreTrendyolReconciliationResolverTests
    : TrendyolReconciliationResolverTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
