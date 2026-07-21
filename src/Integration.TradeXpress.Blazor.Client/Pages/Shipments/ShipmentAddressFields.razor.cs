using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>Tek bir <see cref="ShipmentAddressDto"/> için adres formu (menşei/iade ortak). Kanal-nötr çekirdek:
/// düz metin alanları + üstte <see cref="GeographyCascadePicker"/> (Ülke → İl/Eyalet → İlçe). Picker seçimi serbest
/// metin alanlarını DOLDURUR (kullanıcı override edebilir — fallback). Zorunlu adreste (menşei) İl/Adres alanları
/// " *" ile işaretlenir.</summary>
public partial class ShipmentAddressFields : CrudComponentBase
{
    [Parameter, EditorRequired] public ShipmentAddressDto Model { get; set; } = default!;

    /// <summary>Zorunlu adres mi (menşei) → başlık ve İl/Adres caption'larına " *" eklenir.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>Bölüm başlığı (ör. "Menşei Adresi"). Zorunluysa " *" eklenir.</summary>
    [Parameter] public string? Caption { get; set; }

    // Picker manuel-bağlı → mutasyonu EditContext'e elle bildiririz (dirty/Save; N11AddressFields deseni).
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // DxFormLayoutGroup başlığı — zorunlu adreste " *" ekli.
    private string? GroupCaption => string.IsNullOrEmpty(Caption) ? Caption : (Required ? $"{Caption} *" : Caption);

    // Coğrafya cascade seçimi → adres modelini doldur (il → City, il kodu → CityCode, ilçe → District, ilçe kodu →
    // DistrictCode, mahalle → Neighborhood; ülke → CountryCode). Sembolik ülkede il adı/kodu null gelir (City boşalır
    // — kullanıcı yazabilir). Serbest-metin alanları düzenlenebilir kalır; kullanıcı sonradan override edebilir.
    private void OnGeographySelected(GeographySelection selection)
    {
        Model.CountryCode = selection.CountryCode;
        Model.City = selection.AdministrativeAreaName ?? string.Empty;
        Model.CityCode = selection.AdministrativeAreaCode;
        Model.District = selection.LocalityName;
        Model.DistrictCode = selection.LocalityCode;

        // Coğrafya referansları (additive) — id-only köprü + ISO 3166-2 kodu. Serbest-metin doldurmanın YANINDA taşınır;
        // sembolik ana alanlı ülkede/seçim yokken null gelir (serbest-metin fallback bunlardan etkilenmez).
        Model.AdministrativeAreaId = selection.AdministrativeAreaId;
        Model.LocalityId = selection.LocalityId;
        Model.AdministrativeAreaIsoCode = selection.AdministrativeAreaIsoCode;

        // Mahalle ADI (canlı N11'den; DB'de yalnız ad serbest-metin kalır, SubLocality FK YOK): yalnız seçildiğinde
        // doldur (mahalle seviyesi kullanılmayan ülkede/seçim yokken serbest-metin fallback KORUNUR).
        if (!string.IsNullOrEmpty(selection.NeighborhoodName))
        {
            Model.Neighborhood = selection.NeighborhoodName;
            EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.Neighborhood)));
        }

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.City)));
    }
}
