using Microsoft.Extensions.Localization;
using Integration.TradeXpress.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Integration.TradeXpress;

[Dependency(ReplaceServices = true)]
public class TradeXpressBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<TradeXpressResource> _localizer;

    public TradeXpressBrandingProvider(IStringLocalizer<TradeXpressResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
