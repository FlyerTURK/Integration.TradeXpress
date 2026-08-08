using Microsoft.AspNetCore.Components;

namespace Integration.Framework.Blazor.Client.Components.Shared;

/// <summary>Bilgi bandı — bkz. <c>InfoNotice.razor</c>.</summary>
public partial class InfoNotice
{
    [Parameter, EditorRequired] public string Text { get; set; } = default!;

    [Parameter] public string IconCssClass { get; set; } = FrameworkIcons.Info;
}
