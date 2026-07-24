using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>ShipmentTemplate edit host code-behind — coordinator kurulumu + yeni-kayıt varsayılanları + kargo
/// firması (çekirdek Carrier kataloğu) ve gönderim/iade şubesi (geçerli şirketin aktif şubeleri) picker verisi;
/// hepsi salt seçim. Gönderim/iade adresi = şube seç XOR özel adres (form içinde). Ürün formu combo'sundan inline
/// ekle/düzelt de bu host'u popup'ta açar (IViewOpener).</summary>
public partial class ShipmentTemplateEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] protected ICarrierAppService CarrierAppService { get; set; } = default!;
    [Inject] protected IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<ShipmentTemplateGetDto, ShipmentTemplateListDto, Guid, ShipmentTemplateListRequestDto>? _coordinator;
    private IReadOnlyList<CarrierListDto> _carriers = Array.Empty<CarrierListDto>();
    private IReadOnlyList<BranchListDto> _branches = Array.Empty<BranchListDto>();
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<ShipmentTemplateGetDto, ShipmentTemplateListDto, Guid, ShipmentTemplateListRequestDto, ShipmentTemplateCreateDto, ShipmentTemplateUpdateDto>(
            ShipmentTemplateAppService, Mapper);

        // Kargo firması picker verisi (host-global çekirdek katalog; salt okuma).
        _carriers = await CarrierAppService.GetListAsync();

        // Gönderim/iade şubesi picker verisi (geçerli şirketin aktif şubeleri; server ICurrentCompany ile scope'lar).
        _branches = await BranchAppService.GetMyCompanyBranchesAsync();

        _ready = true;
    }

    // Yeni kayıt varsayılanları: aktif. Süre/ücret alanları GetDto başlangıç değerlerinde (ProcessingDays=1, Free).
    // Gönderim adres modu şube (DispatchAddress null → layout şube modu default'lar); iade kapalı.
    private static void ApplyNew(ShipmentTemplateGetDto model)
    {
        model.IsActive = true;
    }

    // SMART host I/O: DUMB layout'un şube-adres "Kaydet" event'i → şubeyi ANINDA persist et (cross-entity; şablon
    // save'inden bağımsız) → Branches'i tazele → layout ValueObjectEdit özeti güncel adresi yansıtsın.
    private async Task OnBranchAddressSaved((Guid BranchId, BranchAddressDto Address) e)
    {
        await BranchAppService.UpdateAddressAsync(e.BranchId, e.Address);
        _branches = await BranchAppService.GetMyCompanyBranchesAsync();
        StateHasChanged();
    }
}
