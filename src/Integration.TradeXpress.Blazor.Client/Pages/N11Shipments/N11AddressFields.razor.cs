using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.N11Cities;
using Integration.TradeXpress.N11Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.N11Shipments;

/// <summary>Tek bir <see cref="N11ShipmentAddressDto"/> için adres formu (depo/iade ortak). İl seçilince o ilin
/// ilçelerini on-demand çeker; il/ilçe hem KOD hem AD taşır (kod DTO'da, ad görüntü için DTO'ya set edilir).</summary>
public partial class N11AddressFields : CrudComponentBase
{
    [Parameter, EditorRequired] public N11ShipmentAddressDto Model { get; set; } = default!;

    /// <summary>İl listesi — parent (drill) bir kez çeker, tüm adres formlarına paylaştırır.</summary>
    [Parameter] public List<N11CityDto> Cities { get; set; } = new();

    /// <summary>Zorunlu adres mi (depo/iade) → başlık ve alan caption'larına " *" eklenir.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>Bölüm başlığı (ör. "Depo Adresi"). Zorunluysa " *" eklenir.</summary>
    [Parameter] public string? Caption { get; set; }

    [Inject] private IN11CityAppService CityAppService { get; set; } = default!;

    // ValidationEnabled=false combo'lar EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // DxFormLayoutGroup başlığı — zorunlu adreste " *" ekli.
    private string? GroupCaption => string.IsNullOrEmpty(Caption) ? Caption : (Required ? $"{Caption} *" : Caption);

    // Seçili ilin ilçeleri (on-demand). İl değişince yeniden yüklenir.
    private List<N11DistrictDto> _districts = new();

    // Edit'te seçili il varsa ilçeler önceden yüklensin (combo seçili ilçeyi gösterebilsin).
    private string? _loadedDistrictsForCity;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!string.IsNullOrEmpty(Model.CityCode) && _loadedDistrictsForCity != Model.CityCode)
        {
            await LoadDistrictsAsync(Model.CityCode);
        }
    }

    // Coğrafya cascade seçimi → N11 adres modelini doldur (il → City + CityCode [N11 plaka], ilçe → District +
    // DistrictCode, mahalle → Neighborhood; ülke → CountryCode) + additive geo-ref'ler. Ana N11 il/ilçe combo'ları
    // AYNI modele bağlı → seçim onlarda da yansır; yeni ilin ilçeleri combo gösterebilsin diye on-demand yeniden
    // yüklenir. Sembolik ana alanlı ülkede City/CityCode null gelir (kullanıcı yine N11 combo'sundan seçebilir).
    private async Task OnGeographySelected(GeographySelection selection)
    {
        Model.CountryCode = selection.CountryCode;
        Model.City = selection.AdministrativeAreaName ?? string.Empty;
        Model.CityCode = selection.AdministrativeAreaCode;
        Model.District = selection.LocalityName;
        Model.DistrictCode = selection.LocalityCode;

        // Coğrafya referansları (additive) — id-only köprü + ISO 3166-2 kodu. N11 push OKUMAZ (hâlâ City/District/kod
        // okur); yalnız zenginleştirme (fatura/UBL) için taşınır. Sembolik/seçimsiz durumda null gelir.
        Model.AdministrativeAreaId = selection.AdministrativeAreaId;
        Model.LocalityId = selection.LocalityId;
        Model.AdministrativeAreaIsoCode = selection.AdministrativeAreaIsoCode;

        // Mahalle ADI yalnız seçildiğinde doldurulur (mahalle seviyesi kullanılmayan ülkede serbest-metin fallback korunur).
        if (!string.IsNullOrEmpty(selection.NeighborhoodName))
        {
            Model.Neighborhood = selection.NeighborhoodName;
        }

        // Ana N11 ilçe combo'su seçili ilin ilçelerini gösterebilsin diye on-demand yükle (picker CityCode'u değiştirdi).
        if (!string.IsNullOrEmpty(Model.CityCode))
        {
            if (_loadedDistrictsForCity != Model.CityCode)
            {
                await LoadDistrictsAsync(Model.CityCode);
            }
        }
        else
        {
            _districts = new();
            _loadedDistrictsForCity = null;
        }

        NotifyChanged();
    }

    // İl değişti → kod + ad (görüntü) set et, ilçeyi temizle, yeni ilin ilçelerini yükle.
    private async Task OnCityChanged(string? cityCode)
    {
        Model.CityCode = cityCode;
        Model.City = Cities.FirstOrDefault(c => c.CityCode == cityCode)?.Name ?? string.Empty;
        Model.DistrictCode = null;
        Model.District = null;
        _districts = new();
        _loadedDistrictsForCity = null;
        NotifyChanged();
        if (!string.IsNullOrEmpty(cityCode))
        {
            await LoadDistrictsAsync(cityCode);
        }
    }

    // İlçe değişti → kod + ad (görüntü) set et.
    private void OnDistrictChanged(string? districtId)
    {
        Model.DistrictCode = districtId;
        Model.District = _districts.FirstOrDefault(d => d.DistrictId == districtId)?.Name;
        NotifyChanged();
    }

    // EditContext'e "değişti" bildir → drill dirty'yi yeniden değerlendirir, Save enabled olur.
    private void NotifyChanged()
    {
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.CityCode)));
    }

    private async Task LoadDistrictsAsync(string cityCode)
    {
        _districts = await CityAppService.GetDistrictsAsync(cityCode);
        _loadedDistrictsForCity = cityCode;
    }
}
