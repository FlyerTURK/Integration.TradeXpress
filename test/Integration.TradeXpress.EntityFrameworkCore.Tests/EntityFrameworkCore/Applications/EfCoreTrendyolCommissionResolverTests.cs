using Integration.TradeXpress.TrendyolCategories;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreTrendyolCommissionResolverTests : TrendyolCommissionResolverTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
