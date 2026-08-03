using Integration.TradeXpress.ChannelQuestions;
using Xunit;

namespace Integration.TradeXpress.EntityFrameworkCore.Applications;

[Collection(TradeXpressTestConsts.CollectionDefinitionName)]
public class EfCoreChannelQuestionAppServiceTests
    : ChannelQuestionAppServiceTests<TradeXpressEntityFrameworkCoreTestModule>
{
}
