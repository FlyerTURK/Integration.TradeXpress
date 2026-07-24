using Integration.TradeXpress.Goods;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreGoodHostCatalogPreviewTests : GoodHostCatalogPreviewTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
