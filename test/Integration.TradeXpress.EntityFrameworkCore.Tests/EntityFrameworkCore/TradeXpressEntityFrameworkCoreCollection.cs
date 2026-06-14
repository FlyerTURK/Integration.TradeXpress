using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore;

[CollectionDefinition(TradeXpressTestConsts.CollectionDefinitionName)]
public class TradeXpressEntityFrameworkCoreCollection : ICollectionFixture<TradeXpressEntityFrameworkCoreFixture>
{

}
