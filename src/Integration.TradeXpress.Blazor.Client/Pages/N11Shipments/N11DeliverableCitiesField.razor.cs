using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Shipments;

/// <summary>Gönderilecek iller alanı — "Tüm illere gönder" switch'i + (kapalıysa) il checkbox listesi. N11 modeli:
/// boş <see cref="N11ShipmentTemplateDto.DeliverableCityCodes"/> = tüm iller. TagBox yerine yorgunluğu azaltan çözüm.</summary>
public partial class N11DeliverableCitiesField : CrudComponentBase
{
    [Parameter, EditorRequired] public N11ShipmentTemplateDto Model { get; set; } = default!;

    /// <summary>İl listesi (drill bir kez çeker).</summary>
    [Parameter] public List<N11CityDto> Cities { get; set; } = new();

    // ValidationEnabled=false editörler EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // Kısıtlı mı (yalnız seçili iller) yoksa tüm iller mi? Açılışta seçili il varsa kısıtlıdır.
    private bool _restrict;

    protected override void OnInitialized()
    {
        _restrict = Model.DeliverableCityCodes.Count > 0;
    }

    // Switch (@bind-Checked → CheckedExpression otomatik): açık = tüm iller (listeyi boşalt), kapalı = kısıtlı (checkbox listesi).
    private bool AllCities
    {
        get => !_restrict;
        set
        {
            _restrict = !value;
            if (value)
            {
                Model.DeliverableCityCodes = new();
            }

            NotifyChanged();
        }
    }

    private void OnCitiesChanged(IEnumerable<string> values)
    {
        Model.DeliverableCityCodes = values.ToList();
        NotifyChanged();
    }

    // EditContext'e "değişti" bildir → drill dirty'yi (JSON snapshot) yeniden değerlendirir, Save enabled olur.
    private void NotifyChanged()
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.DeliverableCityCodes)));
    }
}
