using Xunit;

using Integration.TradeXpress.Financials.Parities;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreParityAppServiceTests : ParityAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
