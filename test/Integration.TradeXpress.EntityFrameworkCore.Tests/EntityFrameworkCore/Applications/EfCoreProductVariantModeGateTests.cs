using Integration.TradeXpress.Products;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreProductVariantModeGateTests : ProductVariantModeGateTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
