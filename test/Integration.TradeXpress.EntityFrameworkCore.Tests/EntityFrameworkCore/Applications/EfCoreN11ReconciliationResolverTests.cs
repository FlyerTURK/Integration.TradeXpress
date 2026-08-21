using Integration.TradeXpress.Orchestration;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreN11ReconciliationResolverTests
    : N11ReconciliationResolverTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
