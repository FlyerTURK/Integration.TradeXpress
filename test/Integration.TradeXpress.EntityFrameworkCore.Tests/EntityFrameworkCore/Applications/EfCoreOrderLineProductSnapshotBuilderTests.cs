using Integration.TradeXpress.EntityFrameworkCore;
using Xunit;

namespace Integration.TradeXpress.Orders;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreOrderLineProductSnapshotBuilderTests
    : OrderLineProductSnapshotBuilderTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
