using System;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>
/// <see cref="EditShell"/> header buton seti (bayrak). Üç popup kabuğunun gözlenen setleri iki preset'e iner:
/// Edit/Drill = <c>Minimize | Fullscreen</c>; Global (IViewOpener→PopupService popup'ı) = <c>+ Modal + Dock</c>
/// (Modal-toggle ve sekmeye-dock yalnız orada anlamlı). Header padding'i buton sayısından türetilir.
/// </summary>
[Flags]
public enum EditShellButtons
{
    None = 0,
    Minimize = 1,
    Fullscreen = 2,
    Modal = 4,
    Dock = 8
}
