using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Theming;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Layout;

public partial class EmptyLayout
{
    [Inject] private ISizeModeService SizeModeService { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        // Anonim dal: SizeModeService kimlik yoksa yalnız tx.last_size cookie'sini okur (sunucuya gitmez).
        try { await SizeModeService.InitializeAsync(); }
        catch { /* login görünümü varsayılan boyutta kalır — akışı bozma */ }
    }
}
