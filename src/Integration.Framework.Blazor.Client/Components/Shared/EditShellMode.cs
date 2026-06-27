namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// <see cref="EditShell"/> kabuğunun render modu.
/// </summary>
public enum EditShellMode
{
    /// <summary>Chrome (DxPopup) YOK — yalnız gövde (ChildContent) render edilir. Sayfa/split/embedded yolu;
    /// SplitView parked + tek-toolbar bu sayede bozulmaz.</summary>
    Inline,

    /// <summary>Popup chrome: DxPopup sarmalı + header (EditHeaderView) + buton seti + pencere durumu.</summary>
    Popup
}
