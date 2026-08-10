using Integration.TradeXpress.SalesChannelProducts;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSalesChannelProductAppServiceTests
    : SalesChannelProductAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
