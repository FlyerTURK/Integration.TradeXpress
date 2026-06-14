using Integration.TradeXpress.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Integration.TradeXpress.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class TradeXpressController : AbpControllerBase
{
    protected TradeXpressController()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }
}
