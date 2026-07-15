using System;
using System.Collections.Generic;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Metals;
using Integration.TradeXpress.Vouchers;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Metals;

/// <summary>Metal DUMB Layout code-behind — MetalGetDto. FollowingUnit ZORUNLU + Factor(&gt;0) + işçilik +
/// sikke/adet. Görsel ANA seviyeden KALDIRILDI → görseller varyant-başı (EntityVariantsPanel ShowImages).</summary>
public partial class MetalLayout
{
    [Parameter, EditorRequired] public MetalGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>"Varyantları Oluştur" — layout DUMB (servis çağırmaz): host yapar (MetalAppService.GenerateVariantsAsync → Model.Variants).</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }

    private record LaborTypeItem(MetalLaborType Value, string Label);
    private List<LaborTypeItem> _laborTypes = new();

    protected override void OnInitialized()
    {
        _laborTypes = new()
        {
            new(MetalLaborType.Amount,   L["Enum:MetalLaborType:Amount"].Value),
            new(MetalLaborType.Quantity, L["Enum:MetalLaborType:Quantity"].Value),
        };
    }
}
