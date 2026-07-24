using System.Collections.Generic;
using System.Linq;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
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

    /// <summary>İl listesi (drill bir kez çeker) — teslimat illeri seçimi için (adres formları artık paylaşılan
    /// çekirdek coğrafya picker'ını kullanır, bu listeyi tüketmez).</summary>
    [Parameter] public List<N11CityDto> Cities { get; set; } = new();

    /// <summary>Kargo firmaları (host-global; drill bir kez çeker) — firma tag box + iade firması combo için.</summary>
    [Parameter] public List<N11ShipmentCompanyDto> ShipmentCompanies { get; set; } = new();

    // ValidationEnabled=false tag box EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // Depo/iade özel adresi düzenleme popup görünürlüğü (ValueObjectEdit ✎ → popup deseni; ShipmentTemplateLayout).
    private bool _warehouseAddressPopupVisible;
    private bool _exchangeAddressPopupVisible;

    private void OnCompaniesChanged(IEnumerable<string> values)
    {
        Model.ShipmentCompanyExternalIds = values.ToList();
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ShipmentCompanyExternalIds)));
    }

    // Adres özeti (ValueObjectEdit DisplayProjection) — "İl / İlçe / Mahalle, Cadde" (boş atlar). Ortak formatter (DRY).
    private string? AddressSummary(N11ShipmentAddressDto address)
    {
        return AddressDisplay.Summary(address);
    }

    // Adres "boş" mu (ValueObjectEdit EmptyPredicate) — İl + Açık Adres boşsa boş sayılır → placeholder gösterilir.
    private bool IsAddressEmpty(N11ShipmentAddressDto? address)
    {
        return AddressDisplay.IsEmpty(address);
    }

    // ✎ → depo özel adres popup'ını aç.
    private void OpenWarehouseAddressPopup()
    {
        _warehouseAddressPopupVisible = true;
    }

    // ✎ → iade/değişim özel adres popup'ını aç.
    private void OpenExchangeAddressPopup()
    {
        _exchangeAddressPopupVisible = true;
    }
}
