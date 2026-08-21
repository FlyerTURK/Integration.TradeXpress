using System;
using System.Collections.Generic;
using Integration.TradeXpress.AddOns;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.AddOns;

/// <summary>AddOn DUMB layout code-behind — Model bağlama + para birimi lookup verisi (host'tan param).
/// CurrencyUnitId ZORUNLU (Guid); combo TValue="Guid?" ile çalıştığından nullable adapter üzerinden bağlanır
/// (boş seçim = Guid.Empty; zorunluluğu sunucu entity ctor'ında doğrular).</summary>
public partial class AddOnLayout
{
    [Parameter, EditorRequired] public AddOnGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Para birimi lookup verisi — host yükler (DUMB layout servis çağırmaz).</summary>
    [Parameter] public IReadOnlyList<CurrencyUnitListDto> CurrencyUnits { get; set; } = Array.Empty<CurrencyUnitListDto>();

    /// <summary>Inline döviz ekle/düzelt sonrası lookup listesini host tazeler (EntityChange tetikler).</summary>
    [Parameter] public EventCallback OnReloadCurrencyUnits { get; set; }

    // Zorunlu Guid alanı ile TValue="Guid?" combo arasındaki uyarlama (boş = Guid.Empty).
    private Guid? CurrencyUnitIdNullable
    {
        get
        {
            return Model.CurrencyUnitId == Guid.Empty ? null : Model.CurrencyUnitId;
        }
        set
        {
            Model.CurrencyUnitId = value ?? Guid.Empty;
        }
    }
}
