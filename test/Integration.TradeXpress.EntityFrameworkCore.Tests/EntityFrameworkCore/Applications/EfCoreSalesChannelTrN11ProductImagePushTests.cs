using Integration.TradeXpress.EntityFrameworkCore;
using Xunit;

namespace Integration.TradeXpress.N11Products;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSalesChannelTrN11ProductImagePushTests
    : SalesChannelTrN11ProductImagePushTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
