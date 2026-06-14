using Integration.TradeXpress.Companies;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreCompanyAppServiceTests : CompanyAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
