using System;
using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>ShipmentTemplate DUMB layout code-behind — Model bağlama + iade adresi yaşam döngüsü. İade kabulü
/// açılınca iade adresi lazy new() edilir (form onu bağlayabilsin), kapanınca temizlenir (sunucuya boş adres
/// gitmesin). Switch ValidationEnabled=false (manuel bağlı) → değişimi EditContext'e elle bildirir (dirty).</summary>
public partial class ShipmentTemplateLayout : CrudComponentBase
{
    [Parameter, EditorRequired] public ShipmentTemplateGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Kargo firması picker verisi — host yükler (çekirdek Carrier kataloğu; salt seçim). MetalLayout
    /// CurrencyUnits deseni.</summary>
    [Parameter] public IReadOnlyList<CarrierListDto> Carriers { get; set; } = Array.Empty<CarrierListDto>();

    // Manuel-bağlı switch EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz (N11 deseni).
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // İade kabulü değişti → adresi aç/kapat (açıkta lazy new; kapanışta temizle) ve dirty bildir.
    private void OnReturnAcceptedChanged(bool accepted)
    {
        Model.ReturnAccepted = accepted;
        if (accepted)
        {
            Model.ReturnAddress ??= new ShipmentAddressDto();
        }
        else
        {
            Model.ReturnAddress = null;
        }

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ReturnAccepted)));
    }
}
