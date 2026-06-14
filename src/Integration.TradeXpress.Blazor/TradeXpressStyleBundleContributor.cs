using Volo.Abp.AspNetCore.Mvc.UI.Bundling;

namespace Integration.TradeXpress.Blazor;

public class TradeXpressStyleBundleContributor : BundleContributor
{
    public override void ConfigureBundle(BundleConfigurationContext context)
    {
        context.Files.Add(new BundleFile("main.css", true));
    }
}
