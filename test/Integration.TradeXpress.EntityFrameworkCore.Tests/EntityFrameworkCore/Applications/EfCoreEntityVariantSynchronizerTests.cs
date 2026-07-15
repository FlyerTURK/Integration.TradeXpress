using Integration.TradeXpress.Variants;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreEntityVariantSynchronizerTests : EntityVariantSynchronizerTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
