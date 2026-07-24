using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Kargo takip no'sunu gösterir; firma harici takip URL'si (<see cref="CargoTrackingUrlCatalog"/>) destekliyorsa
/// no'yu TIKLANABİLİR yapar ve tıklanınca büyük bir popup açar — takip SAYFASINI popup İÇİNDE iframe ile gömer (odak app'te
/// kalır; header'da emniyet "yeni sekmede aç" linki). Desteklemiyorsa düz metin. Takip no boşsa hiçbir şey çizmez.
/// Yeniden kullanılabilir — sipariş kalem drill'i / başlık tüketir.</summary>
public partial class TrackingNumberDisplay
{
    /// <summary>Kargo firması adı — URL çözümü + popup başlığı için.</summary>
    [Parameter] public string? CarrierName { get; set; }

    /// <summary>Gönderi takip numarası.</summary>
    [Parameter] public string? TrackingNumber { get; set; }

    // Firma+no'dan çözülen harici takip URL'si; null = firma desteklemiyor → düz metin.
    private string? _url;
    private bool _popupVisible;

    protected override void OnParametersSet()
    {
        _url = CargoTrackingUrlCatalog.ResolveTrackingUrl(CarrierName, TrackingNumber);
    }

    private void OpenPopup()
    {
        _popupVisible = true;
    }
}
