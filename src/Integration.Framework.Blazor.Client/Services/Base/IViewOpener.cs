using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Integration.Framework.Blazor.Client.Services.Base;

/// <summary>
/// Görünüm açma soyutlaması (edit ve liste) — çağıran "nerede açılacağını" bilmez.
/// XAF'taki TargetWindow/ShowViewStrategy'nin Blazor karşılığı.
/// </summary>
public interface IViewOpener
{
    /// <summary>
    /// Edit bileşenini uygun hedefte (popup/MDI sekme) açar.
    /// </summary>
    Task OpenAsync(
        Type editComponentType,
        object? id,
        string title,
        string? iconCssClass = null,
        Dictionary<string, object>? extraParams = null);
}
