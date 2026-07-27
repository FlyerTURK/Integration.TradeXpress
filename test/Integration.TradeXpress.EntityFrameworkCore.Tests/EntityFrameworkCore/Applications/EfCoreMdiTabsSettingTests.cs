using Integration.TradeXpress.Settings;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreMdiTabsSettingTests : MdiTabsSettingTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
