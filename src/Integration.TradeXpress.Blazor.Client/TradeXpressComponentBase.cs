using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Localization;
using Volo.Abp.AspNetCore.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.Framework.Blazor.Client.Extensions;

namespace Integration.TradeXpress.Blazor.Client;

public abstract class TradeXpressComponentBase : AbpComponentBase
{
    [Inject]
    protected IJSRuntime JsRuntime { get; set; } = default!;

    [Inject]
    protected ITradeXpressUiService UiService { get; set; } = default!;

    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    protected bool IsLoading { get; set; }

    protected TradeXpressComponentBase()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    protected async Task SafeExecuteAsync(Func<Task> action)
    {
        if (IsLoading)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StateHasChanged();
            await action();
        }
        catch (System.Exception ex)
        {
            await HandleErrorAsync(ex);
            UiService?.ShowErrorToast(ex.Message);
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    protected string? GetQueryParam(string key)
        => NavigationManager.GetQueryParam(key);
}
