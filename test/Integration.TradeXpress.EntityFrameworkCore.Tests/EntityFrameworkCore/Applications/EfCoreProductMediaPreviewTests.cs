using Integration.TradeXpress.EntityFrameworkCore;
using Xunit;

namespace Integration.TradeXpress.Products;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreProductMediaPreviewTests
    : ProductMediaPreviewTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
