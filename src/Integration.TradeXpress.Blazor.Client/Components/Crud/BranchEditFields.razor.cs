using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Crud;

/// <summary>Şube form ALANLARI (PAYLAŞILAN) code-behind — standalone (BranchLayout) + Company şube-drill'i
/// (CompanyBranchDrill) AYNI bu alanları tüketir (DRY). Adres editörü (ValueObjectEdit ✎ → popup) burada yaşar →
/// her iki yüzeyde de görünür/düzenlenir; kayıt yüzeyin kendi save'ine (standalone → branch, graf → company) bağlı.</summary>
public partial class BranchEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public BranchGetDto Model { get; set; } = default!;
    [Parameter] public List<CurrencyUnitListDto> Units { get; set; } = new();
    [Parameter] public bool HeadquartersEnabled { get; set; } = true;
    [Parameter] public bool CodeEnabled { get; set; } = true;

    /// <summary>Inline combo'dan birim eklenince/güncellenince host'un birim listesini (<see cref="Units"/>) tazelemesi
    /// için yukarı sinyal. Bağlanmazsa combo yine çalışır; sadece yeni birim listeye anında düşmez.</summary>
    [Parameter] public EventCallback OnReferenceDataReload { get; set; }

    // Şube adresi düzenleme popup görünürlüğü (ValueObjectEdit ✎ → popup deseni).
    private bool _addressPopupVisible;

    protected override void OnParametersSet()
    {
        // AddressFields non-null model'e bind eder; adres yoksa (mevcut/yeni şube) boş DTO ile başlat → editör hep
        // görünür, boş şubede ilk adres girilebilir. Sessiz init (dirty tetiklemez); boş kalırsa server null'a indirger.
        if (Model is not null && Model.Address is null)
        {
            Model.Address = new BranchAddressDto();
        }
    }

    // Adres özeti (ValueObjectEdit DisplayProjection) — "İl / İlçe / Mahalle, Cadde" (boş atlar). Ortak formatter (DRY).
    private string? AddressSummary(BranchAddressDto address)
    {
        return AddressDisplay.Summary(address);
    }

    // Adres "boş" mu (ValueObjectEdit EmptyPredicate) — İl + Açık Adres boşsa boş sayılır → placeholder gösterilir.
    private bool IsAddressEmpty(BranchAddressDto? address)
    {
        return AddressDisplay.IsEmpty(address);
    }

    // ✎ → şube adres popup'ını aç.
    private void OpenAddressPopup()
    {
        _addressPopupVisible = true;
    }
}
