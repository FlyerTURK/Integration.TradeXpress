using Integration.TradeXpress.Orders;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreOrderPendingDecisionListTests : OrderPendingDecisionListTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
