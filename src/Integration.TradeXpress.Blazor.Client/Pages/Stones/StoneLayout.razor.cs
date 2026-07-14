using System;
using System.Collections.Generic;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Stones;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Stones;

/// <summary>Taş kartı (dumb Layout) — StoneGetDto'ya bind eder; servis çağırmaz (lookup verisi + "Varyantları
/// Oluştur" host'tan gelir). Agnostik graf sekmeleri (Nitelik/Varyant/Görsel/Doküman/Not) formun en sonunda; fiyat
/// taş seviyesinde (varyant-başı fiyat uzantısı YOK).</summary>
public partial class StoneLayout
{
    [Parameter, EditorRequired] public StoneGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>"Varyantları Oluştur" — layout DUMB (servis çağırmaz): işi host yapar (StoneAppService.GenerateVariantsAsync → Model.Variants).</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }
}
