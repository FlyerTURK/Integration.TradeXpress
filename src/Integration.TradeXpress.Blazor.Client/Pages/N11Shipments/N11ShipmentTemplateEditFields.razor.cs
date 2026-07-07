using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Shipments;

/// <summary>N11 kargo şablonu tam edit formu — DrillList <c>EditContent</c>'i içinde açılır. Şartlı kargo düzenlenebilir
/// (push ile N11'e yazılır). Firma tag box'ı ValidationEnabled=false olduğundan değişimi EditContext'e elle bildirir (dirty).</summary>
public partial class N11ShipmentTemplateEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public N11ShipmentTemplateDto Model { get; set; } = default!;

    /// <summary>İl listesi (drill bir kez çeker) — teslimat illeri + adres formları için.</summary>
    [Parameter] public List<N11CityDto> Cities { get; set; } = new();

    /// <summary>Kargo firmaları (host-global; drill bir kez çeker) — firma tag box + iade firması combo için.</summary>
    [Parameter] public List<N11ShipmentCompanyDto> ShipmentCompanies { get; set; } = new();

    // ValidationEnabled=false tag box EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    private void OnCompaniesChanged(IEnumerable<string> values)
    {
        Model.ShipmentCompanyExternalIds = values.ToList();
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ShipmentCompanyExternalIds)));
    }
}
