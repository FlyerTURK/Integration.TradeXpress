using System;
using Integration.Framework.Addressing;
using Integration.Framework.Blazor.Client.Components.Crud;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Components.Shared;

/// <summary>Yeniden-kullanılabilir TEMİZ adres formu — herhangi bir <see cref="IAddressEditModel"/>'e bind eder.
/// Üstte <see cref="GeographyCascadePicker"/> (İl/İlçe/Mahalle + kodlar + id-only köprüler + CountryCode'u tek
/// kaynaktan DOLDURUR); serbest-metin: Line + UBL zenginleştirme alanları (BuildingName/BuildingNumber/Room/Floor/
/// PostalCode/Postbox/AdditionalStreetName/Title). <see cref="FixedCountryId"/> ile ülke kilidi (picker'a geçer).</summary>
public partial class AddressFields : CrudComponentBase
{
    /// <summary>Düzenlenen adres modeli (flat; picker doldurur + serbest-metin alanları).</summary>
    [Parameter, EditorRequired] public IAddressEditModel Model { get; set; } = default!;

    /// <summary>Ülke kilidi — doluysa picker o ülkeye kilitlenir (ülke combo'su gizli). null → serbest mod.</summary>
    [Parameter] public Guid? FixedCountryId { get; set; }

    /// <summary>Ülke VARSAYILANI (kilit DEĞİL) — <see cref="FixedCountryId"/> ve mevcut adres ülkesi BOŞ iken picker
    /// bu ülkeyi ön-seçer + cascade tetikler (adres company ülkesiyle dolar); kullanıcı Ülke combosundan değiştirebilir.
    /// Sınır-ötesi özel gönderim/iade adresi için (ülke serbest ama company ülkesine ön-dolu). null → varsayılan yok.</summary>
    [Parameter] public Guid? DefaultCountryId { get; set; }

    /// <summary>Bölüm başlığı (ör. "Şube Adresi").</summary>
    [Parameter] public string? Caption { get; set; }

    /// <summary>EMBEDDED (picker deseniyle aynı): true iken kendi <c>DxFormLayout</c> wrapper'ını RENDER ETMEZ, yalnız
    /// item'ları render eder → parent'ın TEK DxFormLayout'una iner (party+adres aynı caption sütununda HİZALI). false
    /// (varsayılan) → kendi DxFormLayout'unu taşır (standalone popup — branch/kargo şablonu bu modu kullanır).</summary>
    [Parameter] public bool Embedded { get; set; }

    // Picker ValidationEnabled=false combolar EditContext'e bildirmez → dirty/Save için mutasyonu elle bildiririz.
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // Coğrafya cascade seçimi → adres modelini doldur (il → City + CityCode, ilçe → District + DistrictCode,
    // mahalle → Neighborhood; ülke → CountryCode) + additive geo-ref'ler (id-only köprü + ISO 3166-2 kodu).
    // Serbest-metin (Line/PostalCode/Title) ETKİLENMEZ. Sembolik ana alanlı ülkede City null gelir; mahalle
    // seviyesi kullanılmayan ülkede Neighborhood korunur (yalnız seçilince doldurulur).
    private void OnGeographySelected(GeographySelection selection)
    {
        Model.CountryCode = selection.CountryCode;
        Model.CountryName = selection.CountryName;   // salt görüntü — özet kod yerine adı gösterir
        Model.City = selection.AdministrativeAreaName ?? string.Empty;
        Model.CityCode = selection.AdministrativeAreaCode;
        Model.District = selection.LocalityName;
        Model.DistrictCode = selection.LocalityCode;

        Model.AdministrativeAreaId = selection.AdministrativeAreaId;
        Model.LocalityId = selection.LocalityId;
        Model.AdministrativeAreaIsoCode = selection.AdministrativeAreaIsoCode;

        // Mahalle: ülke bu seviyeyi KULLANIYORSA gelen değer aynen yazılır — null da dahil, çünkü kullanıcı
        // mahalleyi TEMİZLEMİŞ olabilir (serbest metin girişiyle mümkün). Ülke bu seviyeyi kullanmıyorsa combo
        // gizlidir ve seçim taşımaz → mevcut değer KORUNUR (eskiden bu ayrım yoktu: boş gelen her değer korunuyordu,
        // dolayısıyla temizleme işlemi sessizce yok sayılıyordu).
        if (selection.UsesSubLocality)
        {
            Model.Neighborhood = selection.NeighborhoodName;
        }

        // EditContext'e "değişti" bildir → drill/form dirty'yi yeniden değerlendirir, Save enabled olur.
        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.City)));
    }
}
