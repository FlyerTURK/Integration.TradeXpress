using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Shipments;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.Shipments;

/// <summary>ShipmentTemplate edit host code-behind — coordinator kurulumu + yeni-kayıt varsayılanları + kargo
/// firması picker verisi (çekirdek Carrier kataloğu; salt seçim). Menşei/iade adresi form içinde düz metin
/// (kanal-nötr çekirdek). Ürün formu combo'sundan inline ekle/düzelt de bu host'u popup'ta açar (IViewOpener).</summary>
public partial class ShipmentTemplateEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected IShipmentTemplateAppService ShipmentTemplateAppService { get; set; } = default!;
    [Inject] protected ICarrierAppService CarrierAppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<ShipmentTemplateGetDto, ShipmentTemplateListDto, Guid, ShipmentTemplateListRequestDto>? _coordinator;
    private IReadOnlyList<CarrierListDto> _carriers = Array.Empty<CarrierListDto>();
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<ShipmentTemplateGetDto, ShipmentTemplateListDto, Guid, ShipmentTemplateListRequestDto, ShipmentTemplateCreateDto, ShipmentTemplateUpdateDto>(
            ShipmentTemplateAppService, Mapper);

        // Kargo firması picker verisi (host-global çekirdek katalog; salt okuma).
        _carriers = await CarrierAppService.GetListAsync();

        _ready = true;
    }

    // Yeni kayıt varsayılanları: aktif. Süre/ücret alanları GetDto başlangıç değerlerinde (ProcessingDays=1, Free);
    // menşei adres new() ile init. (İade adresi ReturnAccepted açılınca layout tarafından oluşturulur.)
    private static void ApplyNew(ShipmentTemplateGetDto model)
    {
        model.IsActive = true;
    }
}
