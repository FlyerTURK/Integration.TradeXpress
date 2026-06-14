using Integration.TradeXpress.Samples;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Domains;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<TradeXpressEntityFrameworkCoreTestModule>
{

}
