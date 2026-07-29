using System;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Integration.TradeXpress.Blazor.Client.Components.Mdi;

public partial class MdiTabPane : IDisposable
{
    [Inject] private IStringLocalizer<TradeXpressResource> L { get; set; } = default!;

    [Parameter, EditorRequired] public MdiTab Tab { get; set; } = default!;

    private TabContentLoad? _subscribed;

    protected override void OnParametersSet()
    {
        // Abonelik parametreye göre kurulur: aynı bileşen örneği başka bir sekmeye yeniden
        // bağlanırsa eski sekmenin olayında asılı kalmasın.
        if (ReferenceEquals(_subscribed, Tab.Load))
        {
            return;
        }

        Unsubscribe();
        _subscribed = Tab.Load;
        _subscribed.Changed += OnLoadChanged;
    }

    /// <summary>Yükleme durumu sayfa bileşeninin İÇİNDEN değişiyor (bileti CrudLayout kapatıyor);
    /// panel burada yaşadığı için re-render'ı bu bileşen tetiklemeli. Olay arka plan thread'inden
    /// gelebileceğinden dispatcher'a alınır.</summary>
    private void OnLoadChanged()
    {
        _ = InvokeAsync(StateHasChanged);
    }

    private void Unsubscribe()
    {
        if (_subscribed != null)
        {
            _subscribed.Changed -= OnLoadChanged;
            _subscribed = null;
        }
    }

    public void Dispose()
    {
        Unsubscribe();
    }
}
