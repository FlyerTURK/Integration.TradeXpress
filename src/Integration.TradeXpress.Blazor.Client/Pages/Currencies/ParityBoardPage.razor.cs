using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Integration.TradeXpress.Currencies;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Currencies;

/// <summary>
/// Parite panosu — aktif paritelerin canlı çapraz kurları (birimlerin efektifinin bid/ask
/// çaprazı). Read-only; feed gelene kadar fiyatı olmayan çiftler listelenmez.
/// </summary>
public partial class ParityBoardPage
{
    public ParityBoardPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject] protected IParityAppService ParityAppService { get; set; } = default!;

    protected List<ParityBoardDto> Parities { get; set; } = new();

    private PeriodicTimer? _timer;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        _ = AutoRefreshLoopAsync();
    }

    private async Task AutoRefreshLoopAsync()
    {
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(8));
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                await LoadAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    protected async Task LoadAsync()
    {
        Parities = await ParityAppService.GetBoardAsync();
    }

    public void Dispose() => _timer?.Dispose();
}
