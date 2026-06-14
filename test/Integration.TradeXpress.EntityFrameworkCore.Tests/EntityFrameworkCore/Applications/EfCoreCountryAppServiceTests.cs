using Integration.TradeXpress.Countries;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreCountryAppServiceTests : CountryAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
