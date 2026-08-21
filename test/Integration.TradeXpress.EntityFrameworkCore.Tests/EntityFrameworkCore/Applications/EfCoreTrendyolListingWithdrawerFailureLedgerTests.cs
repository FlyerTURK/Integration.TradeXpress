using Integration.TradeXpress.TrendyolProducts;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreTrendyolListingWithdrawerFailureLedgerTests
    : TrendyolListingWithdrawerFailureLedgerTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
