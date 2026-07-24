using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Components.Shared;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>ShipmentTemplate DUMB layout code-behind — Model bağlama + gönderim/iade adres MODU (şube XOR özel)
/// yaşam döngüsü. Mod switch'i toggle edilince model tutarlı tutulur: şube modu → gömülü ÖZEL adres null'lanır;
/// özel mod → şube id null'lanır + adres lazy new() edilir (form bağlayabilsin). İade "gönderimle aynı" açıkken
/// ayrı şube/adres tutulmaz. Switch ValidationEnabled=false (manuel bağlı) → değişimi EditContext'e elle bildirir
/// (dirty). Mod bayrakları (_dispatchUseBranch/_returnUseBranch) yeni Model'e ilk bağlanışta türetilir.</summary>
public partial class ShipmentTemplateLayout : CrudComponentBase
{
    [Parameter, EditorRequired] public ShipmentTemplateGetDto Model { get; set; } = default!;
    [Parameter] public bool IsNew { get; set; }

    /// <summary>Kargo firması picker verisi — host yükler (çekirdek Carrier kataloğu; salt seçim). MetalLayout
    /// CurrencyUnits deseni.</summary>
    [Parameter] public IReadOnlyList<CarrierListDto> Carriers { get; set; } = Array.Empty<CarrierListDto>();

    /// <summary>Gönderim/iade şubesi picker verisi — host yükler (geçerli şirketin aktif şubeleri; salt seçim +
    /// her satır kendi adresini taşır → şube modu ✎ ekstra çağrısız çalışır).</summary>
    [Parameter] public IReadOnlyList<BranchListDto> Branches { get; set; } = Array.Empty<BranchListDto>();

    /// <summary>DUMB layout I/O yapmaz — şube adresi "Kaydet"'te bu event'i yükseltir; SMART host (EditHost)
    /// <c>IBranchAppService.UpdateAddressAsync</c> çağırıp şubeyi anında persist eder + Branches'i tazeler.</summary>
    [Parameter] public EventCallback<(Guid BranchId, BranchAddressDto Address)> OnBranchAddressSaved { get; set; }

    // Manuel-bağlı switch EditContext'e bildirmez → dirty/Save güncellenmez. Değişimde elle bildiririz (N11 deseni).
    [CascadingParameter] private EditContext? EditContext { get; set; }

    // Adres modu bayrakları (UI-only): true = ŞUBE modu, false = ÖZEL adres modu. Yeni Model'e bağlanışta türetilir.
    private bool _dispatchUseBranch = true;
    private bool _returnUseBranch = true;

    // Özel gönderim/iade adresi düzenleme popup görünürlüğü (ValueObjectEdit ✎ → popup deseni).
    private bool _dispatchAddressPopupVisible;
    private bool _returnAddressPopupVisible;

    // PAYLAŞILAN şube-adres popup'ı — aynı anda tek şube adresi düzenlenir (gönderim VEYA iade). ÇALIŞMA KOPYASI
    // (form EditContext'i dışında) + hedef şube id'si. "Kaydet" event'i yükseltir; host persist + tazeler.
    private bool _branchAddressPopupVisible;
    private BranchAddressDto? _branchAddressEdit;
    private Guid _branchAddressEditId;

    // Şube modunda seçili gönderim/iade şubesinin adresi (picker verisinden okunur; null → adres yok / henüz eklenmemiş).
    private BranchAddressDto? DispatchBranchAddress =>
        Model.DispatchBranchId is { } id ? Branches.FirstOrDefault(b => b.Id == id)?.Address : null;

    private BranchAddressDto? ReturnBranchAddress =>
        Model.ReturnBranchId is { } id ? Branches.FirstOrDefault(b => b.Id == id)?.Address : null;

    // Düzenlenen şubenin şirket ülkesi — şube-adres popup'ında ülke KİLİDİ (branch adresi company ülkesinde olmalı).
    private Guid? BranchAddressEditCountryId =>
        Branches.FirstOrDefault(b => b.Id == _branchAddressEditId)?.CompanyCountryId;

    // Company ülkesi — özel gönderim/iade adresinde ülke VARSAYILANI (kilit değil). Şube listesiyle birlikte gelir
    // (hepsi geçerli şirketin şubeleri) → ilk şubeden okunur. Şube yoksa null (varsayılan yok; OrgTree en az 1 kurar).
    private Guid? CompanyCountryId => Branches.Count > 0 ? Branches[0].CompanyCountryId : null;

    // Bayrakları yalnız Model REFERANSI değişince türet (kullanıcı toggle'larını ezmesin). Gömülü ÖZEL adres varsa
    // özel mod, yoksa şube modu (yeni kayıtta ikisi de null → şube modu default).
    private ShipmentTemplateGetDto? _boundModel;

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_boundModel, Model))
        {
            return;
        }

        _boundModel = Model;
        _dispatchUseBranch = Model.DispatchAddress is null;
        _returnUseBranch = Model.ReturnAddress is null;

        // Yeni kayıt açılışı: gönderim şube modunda + seçim boşsa ilk şubeyi ön-seç (şube zorunlu).
        EnsureDispatchBranchDefault();
    }

    // Yeni kayıtta gönderim ŞUBE modu + seçim boş + şube var → ilk şubeyi otomatik seç. Aksi halde no-op (kendini
    // korur: mevcut kayıt, özel-adres modu, dolu seçim ya da boş liste değiştirilmez).
    private void EnsureDispatchBranchDefault()
    {
        if (IsNew && _dispatchUseBranch && Model.DispatchBranchId is null && Branches.Count > 0)
        {
            Model.DispatchBranchId = Branches[0].Id;
        }
    }

    // Yeni kayıtta iade ŞUBE modu (iade açık + gönderimden farklı + şube seçili) + seçim boş + şube var → ilk şubeyi
    // otomatik seç. Görünürlük koşullarını içerir → uygun olmayan durumda güvenle no-op.
    private void EnsureReturnBranchDefault()
    {
        if (IsNew && Model.ReturnAccepted && !Model.ReturnSameAsDispatch && _returnUseBranch
            && Model.ReturnBranchId is null && Branches.Count > 0)
        {
            Model.ReturnBranchId = Branches[0].Id;
        }
    }

    // Gönderim adres modu değişti → tam-biri invariant'ını UI'da koru: şube modu → özel adres null; özel mod → şube
    // id null + adres lazy new (form bağlayabilsin). Dirty bildir.
    private void OnDispatchModeChanged(bool useBranch)
    {
        _dispatchUseBranch = useBranch;
        if (useBranch)
        {
            Model.DispatchAddress = null;
            EnsureDispatchBranchDefault();   // yeni kayıtta şube moduna dönünce ilk şubeyi ön-seç
        }
        else
        {
            Model.DispatchBranchId = null;
            Model.DispatchAddress ??= new ShipmentAddressDto();
        }

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.DispatchBranchId)));
    }

    // İade kabulü değişti → açılışta "gönderimle aynı" default'la (en yalın), tüm ayrı iade alanlarını temizle;
    // kapanışta hepsini temizle. Dirty bildir.
    private void OnReturnAcceptedChanged(bool accepted)
    {
        Model.ReturnAccepted = accepted;
        Model.ReturnSameAsDispatch = accepted;   // açılışta default = gönderim ile aynı
        Model.ReturnBranchId = null;
        Model.ReturnAddress = null;
        _returnUseBranch = true;

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ReturnAccepted)));
    }

    // "Gönderim ile aynı" değişti → aynıysa ayrı şube/adres temizlenir; farklıysa şube modu default'lanır (adres null).
    private void OnReturnSameChanged(bool same)
    {
        Model.ReturnSameAsDispatch = same;
        Model.ReturnBranchId = null;
        Model.ReturnAddress = null;
        _returnUseBranch = true;
        EnsureReturnBranchDefault();   // "farklı" + yeni kayıt → şube modunda ilk şubeyi ön-seç

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ReturnSameAsDispatch)));
    }

    // İade adres modu değişti (yalnız "farklı" iken) → şube modu → adres null; özel mod → şube id null + adres lazy new.
    private void OnReturnModeChanged(bool useBranch)
    {
        _returnUseBranch = useBranch;
        if (useBranch)
        {
            Model.ReturnAddress = null;
            EnsureReturnBranchDefault();   // yeni kayıtta şube moduna dönünce ilk şubeyi ön-seç
        }
        else
        {
            Model.ReturnBranchId = null;
            Model.ReturnAddress ??= new ShipmentAddressDto();
        }

        EditContext?.NotifyFieldChanged(new FieldIdentifier(Model, nameof(Model.ReturnBranchId)));
    }

    // Özel adres özeti (ValueObjectEdit DisplayProjection) — "İl / İlçe / Mahalle, Cadde" (boş atlar). Ortak formatter (DRY).
    private string? AddressSummary(ShipmentAddressDto address)
    {
        return AddressDisplay.Summary(address);
    }

    // Özel adres "boş" mu (ValueObjectEdit EmptyPredicate) — İl + Açık Adres boşsa boş sayılır → placeholder gösterilir.
    private bool IsAddressEmpty(ShipmentAddressDto? address)
    {
        return AddressDisplay.IsEmpty(address);
    }

    // ✎ → gönderim özel adres popup'ını aç.
    private void OpenDispatchAddressPopup()
    {
        _dispatchAddressPopupVisible = true;
    }

    // ✎ → iade özel adres popup'ını aç.
    private void OpenReturnAddressPopup()
    {
        _returnAddressPopupVisible = true;
    }

    // Şube adres özeti/boşluk (ValueObjectEdit) — özel adresle AYNI ortak formatter (DRY); ikisi de IAddressEditModel.
    private string? BranchAddressSummary(BranchAddressDto address) => AddressDisplay.Summary(address);

    private bool IsBranchAddressEmpty(BranchAddressDto? address) => AddressDisplay.IsEmpty(address);

    // ✎ → seçili gönderim/iade şubesinin adresini paylaşılan popup'ta aç.
    private void OpenDispatchBranchAddressPopup()
    {
        if (Model.DispatchBranchId is { } id)
        {
            OpenBranchAddressPopup(id);
        }
    }

    private void OpenReturnBranchAddressPopup()
    {
        if (Model.ReturnBranchId is { } id)
        {
            OpenBranchAddressPopup(id);
        }
    }

    // Şube-adres popup'ını hedef şubeyle aç — mevcut adresin ÇALIŞMA KOPYASINI düzenler (iptal edilebilir); adres yoksa
    // boş yeni model (kullanıcı ilk adresi girebilsin). Ülke popup'ta şubenin şirket ülkesine kilitli.
    private void OpenBranchAddressPopup(Guid branchId)
    {
        _branchAddressEditId = branchId;
        _branchAddressEdit = Branches.FirstOrDefault(b => b.Id == branchId)?.Address?.Clone() ?? new BranchAddressDto();
        _branchAddressPopupVisible = true;
    }

    // "Kaydet" YAN ETKİSİ → DUMB layout persist ETMEZ: event'i yükseltir (host UpdateAddressAsync + Branches tazeleme).
    // Popup'ı KABUK (ValueObjectEditPopup) kapatır → burada görünürlüğe DOKUNMA (çift kapatma olmaz).
    private async Task RaiseBranchAddressSaved()
    {
        if (_branchAddressEdit is null)
        {
            return;
        }

        await OnBranchAddressSaved.InvokeAsync((_branchAddressEditId, _branchAddressEdit));
    }
}
