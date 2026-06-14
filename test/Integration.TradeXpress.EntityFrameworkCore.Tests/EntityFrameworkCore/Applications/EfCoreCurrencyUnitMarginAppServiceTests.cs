using Integration.TradeXpress.Currencies;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreCurrencyUnitMarginAppServiceTests : CurrencyUnitMarginAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
