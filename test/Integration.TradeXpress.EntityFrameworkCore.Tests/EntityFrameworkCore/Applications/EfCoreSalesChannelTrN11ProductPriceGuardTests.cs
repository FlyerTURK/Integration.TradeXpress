using Integration.TradeXpress.N11Products;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSalesChannelTrN11ProductPriceGuardTests
    : SalesChannelTrN11ProductPriceGuardTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
