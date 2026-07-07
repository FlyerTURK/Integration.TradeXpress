using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
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
