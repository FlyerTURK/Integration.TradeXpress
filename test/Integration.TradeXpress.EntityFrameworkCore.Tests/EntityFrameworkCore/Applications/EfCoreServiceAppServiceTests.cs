using Integration.TradeXpress.Services;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreServiceAppServiceTests : ServiceAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
