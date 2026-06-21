using Xunit;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreCurrencyUnitAppServiceTests : CurrencyUnitAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
