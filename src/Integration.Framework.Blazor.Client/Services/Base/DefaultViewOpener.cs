using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Varsayılan IViewOpener: edit görünümünü popup (IPopupService) olarak açar.
/// Sekme açmak için TabViewOpener vb. inject edilebilir.
/// </summary>
public class DefaultViewOpener : IViewOpener
{
    private readonly IPopupService _popup;

    public DefaultViewOpener(IPopupService popup)
    {
        _popup = popup;
    }

    public Task OpenAsync(
        Type editComponentType,
        object? id,
        string title,
        string? iconCssClass = null,
        Dictionary<string, object>? extraParams = null)
    {
        var parameters = new Dictionary<string, object>
        {
            { "Id", id! },
            { "IsPopupMode", true },
        };

        if (extraParams != null)
        {
            foreach (var (k, v) in extraParams)
                parameters[k] = v;
        }

        return _popup.ShowAsync(editComponentType, parameters, new PopupOptions { Title = title });
    }
}
