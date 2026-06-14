using Integration.TradeXpress.Localization;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress;

/* Inherit your application services from this class.
 */
public abstract class TradeXpressAppService : ApplicationService
{
    protected TradeXpressAppService()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }
}
