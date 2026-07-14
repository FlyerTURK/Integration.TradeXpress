using System;
using System.Collections.Generic;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Jewelries;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Jewelries;

/// <summary>Mücevher kartı (dumb Layout) — JewelryGetDto'ya bind eder; servis çağırmaz (lookup verisi + "Varyantları
/// Oluştur" host'tan gelir). Agnostik graf sekmeleri (Nitelik/Varyant/Görsel/Doküman/Not) formun en sonunda; fiyat
/// mücevher seviyesinde (varyant-başı fiyat uzantısı YOK — Good'dan farkı budur).</summary>
public partial class JewelryLayout
{
    [Parameter, EditorRequired] public JewelryGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>"Varyantları Oluştur" — layout DUMB (servis çağırmaz): işi host yapar (JewelryAppService.GenerateVariantsAsync → Model.Variants).</summary>
    [Parameter] public EventCallback OnGenerateVariants { get; set; }
}
