using Integration.TradeXpress.EntityFrameworkCore;
using Xunit;

namespace Integration.TradeXpress.Attachments;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreEntityMediaPushSelectionTests
    : EntityMediaPushSelectionTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
