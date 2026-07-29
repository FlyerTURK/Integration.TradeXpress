using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Satır içi uyarı kutusu — engellemeyen bilgilendirme. İçerik <see cref="ChildContent"/> ile verilir.</summary>
public partial class InlineWarning
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
