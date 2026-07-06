using Integration.TradeXpress.SalesChannels;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSalesChannelAppServiceResolutionTests : SalesChannelAppServiceResolutionTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
