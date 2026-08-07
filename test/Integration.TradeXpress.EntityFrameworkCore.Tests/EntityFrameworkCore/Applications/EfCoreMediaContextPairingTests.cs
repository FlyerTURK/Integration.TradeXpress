using Integration.TradeXpress.Attachments;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreMediaContextPairingTests : MediaContextPairingTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
