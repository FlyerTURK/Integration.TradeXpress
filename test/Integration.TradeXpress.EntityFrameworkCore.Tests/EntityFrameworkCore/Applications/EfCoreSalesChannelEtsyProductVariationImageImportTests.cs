using Integration.TradeXpress.EtsyProducts;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSalesChannelEtsyProductVariationImageImportTests
    : SalesChannelEtsyProductVariationImageImportTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
