using System.Collections.Generic;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Financials.CurrencyUnits;
using Integration.TradeXpress.Vaults;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Components.Crud;

/// <summary>Şube form ALANLARI (PAYLAŞILAN) code-behind — standalone (BranchLayout) + Company şube-drill'i
/// (CompanyBranchDrill) AYNI bu alanları tüketir (DRY). Adres editörü (ValueObjectEdit ✎ → popup) burada yaşar →
/// her ikisinde de görünür/düzenlenir; kayıt çağıranın kendi save'ine (standalone → branch, graf → company) bağlı.</summary>
public partial class BranchEditFields : CrudComponentBase
{
    [Parameter, EditorRequired] public BranchGetDto Model { get; set; } = default!;
    [Parameter] public List<CurrencyUnitListDto> Units { get; set; } = new();
    [Parameter] public bool HeadquartersEnabled { get; set; } = true;
    [Parameter] public bool CodeEnabled { get; set; } = true;

    /// <summary>Şubenin altında GÖMÜLÜ çizilecek merkez kasa (yalnız YENİ kayıt formlarında verilir; null =
    /// kasa bölümü çizilmez). Kasa da şube kadar zorunludur (OrgTreeManager en-az-1-çocuk) — arka planda
    /// sessizce kurulduğu için kullanıcı adını göremiyordu (2026-08-03 Hakan bulgusu). Üç form da
    /// (şirket/tenant/şube onboarding) bu TEK parametreyi besler; kasa markup'ı tek yerde yaşar.</summary>
    [Parameter] public VaultGraphDto? EmbeddedVault { get; set; }

    /// <summary>Grup başlığı. Boş → varsayılan "Genel". Onboarding'de "Merkez Şube" verilir: o formda tek şube
    /// vardır ve merkezdir; "Genel" demek hangi şube olduğunu belirsiz bırakıyordu.</summary>
    [Parameter] public string? GroupCaption { get; set; }

    /// <summary>Açıklama / Durum / Sıra görünsün mü (varsayılanlar yine DTO'da taşınır; yalnız KONTROL gizlenir).</summary>
    [Parameter] public bool ShowAdvancedFields { get; set; } = true;

    /// <summary>Merkez switch'i görünsün mü. Onboarding'de GİZLİ: o formdaki tek şube tanımı gereği merkezdir,
    /// devre dışı bir anahtar göstermek yalnız gürültü olurdu (grup başlığı zaten "Merkez Şube" diyor).</summary>
    [Parameter] public bool ShowHeadquarters { get; set; } = true;

    /// <summary>Bilanço birimi combo'su görünsün mü. Onboarding'de GİZLİ — merkez şube ŞİRKETİN birimini devralır
    /// (kullanıcı kararı). Değer yine DTO'da taşınır; yalnız seçim kontrolü gösterilmez.</summary>
    [Parameter] public bool ShowBaseCurrency { get; set; } = true;

    /// <summary>Adres editörü grubun İÇİNDE mi render edilsin. Onboarding'de true → "Merkez Şube" grubu şubenin
    /// TÜM bilgisini (kod/ad/para birimi/adres) tek çerçevede toplar. Varsayılan false → mevcut formlarda adres
    /// bilinçli olarak grupsuz kalır (ui-blazor sırası: General → Adres → Description → Status).</summary>
    [Parameter] public bool AddressInsideGroup { get; set; }

    /// <summary>Gömülü mod — kendi <c>DxFormLayout</c>'unu render etmez (parent'ın tek layout'una iner).</summary>
    [Parameter] public bool Embedded { get; set; }

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

    // Kod daima BÜYÜK HARF (invariant — sunucudaki NormalizeCode ile aynı çevrim; kültüre duyarlı çevrim
    // Türkçe'de "i"yi "İ" yapıp sunucunun ürettiği "I"dan sapardı).
    private void UpperCaseCode()
    {
        if (!string.IsNullOrEmpty(Model.Code))
        {
            Model.Code = Model.Code.ToUpperInvariant();
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
