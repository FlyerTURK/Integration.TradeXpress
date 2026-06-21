using Xunit;

using Integration.TradeXpress.Financials.ExchangeRates;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreExchangeRateSnapshotWriterTests : ExchangeRateSnapshotWriterTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
