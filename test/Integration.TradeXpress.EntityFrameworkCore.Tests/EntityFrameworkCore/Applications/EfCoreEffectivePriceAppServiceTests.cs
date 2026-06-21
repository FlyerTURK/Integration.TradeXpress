using Xunit;

using Integration.TradeXpress.Financials.CurrencyUnits;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreEffectivePriceAppServiceTests : EffectivePriceAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
