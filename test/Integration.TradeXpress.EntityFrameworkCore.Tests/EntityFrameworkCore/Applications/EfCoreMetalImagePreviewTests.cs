using Integration.TradeXpress.Metals;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreMetalImagePreviewTests : MetalImagePreviewTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
