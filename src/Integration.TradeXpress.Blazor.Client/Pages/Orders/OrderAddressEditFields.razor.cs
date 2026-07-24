using System;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Orders;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Orders;

/// <summary>Sipariş adresi (fatura/teslimat) — kompakt özet + ✎ → popup (ValueObjectEdit deseni; branch/kargo şablonuyla
/// TUTARLI, DRY). Özet = alıcı adı + adres (ithal değer tamamı görünür); popup = alıcı-kimlik alanları + ortak
/// <c>AddressFields</c> geo-picker. Kayıt order form'un save'iyle (Model order edit modelinin parçası).</summary>
public partial class OrderAddressEditFields
{
    [Parameter, EditorRequired] public OrderEditAddressDto Model { get; set; } = default!;

    /// <summary>Adres picker'ının KİLİTLİ ülkesi (TR) — parent (OrderEditLayout) siparişin çözülmüş TR id'sini geçer.
    /// null ise picker serbest-ülke moduna düşer (TR kataloğu yoksa güvenli geri-dönüş).</summary>
    [Parameter] public Guid? FixedCountryId { get; set; }

    /// <summary>Özet item + popup başlığı (Fatura Adresi / Teslimat Adresi) — parent geçer.</summary>
    [Parameter] public string? Caption { get; set; }

    // Adres düzenleme popup görünürlüğü (ValueObjectEdit ✎ → popup deseni).
    private bool _popupVisible;

    // ValueObjectEdit özeti — alıcı adı + adres özeti (ikisi de boşsa atlanır; ortak AddressDisplay formatter, DRY).
    private string? AddressSummary(OrderEditAddressDto address)
    {
        var addressText = AddressDisplay.Summary(address);
        if (string.IsNullOrWhiteSpace(address.FullName))
        {
            return addressText;
        }

        if (string.IsNullOrWhiteSpace(addressText))
        {
            return address.FullName;
        }

        return $"{address.FullName} — {addressText}";
    }

    // Adres "boş" mu (ValueObjectEdit EmptyPredicate) — İl + Açık Adres boşsa boş sayılır → placeholder.
    private bool IsEmpty(OrderEditAddressDto? address)
    {
        return AddressDisplay.IsEmpty(address);
    }

    // ✎ → sipariş adres popup'ını aç.
    private void OpenPopup()
    {
        _popupVisible = true;
    }
}
