using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SpecialCodes;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.SpecialCodes;

/// <summary>Özel Kod edit host — ince host (coordinator + aynı-bağlam parent seçenekleri). Popup-only; bağlam
/// picker'dan extraParams ile gelir.</summary>
public partial class SpecialCodeEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }

    /// <summary>Bağlam — picker'dan gelir (yeni kaydın EntityName/PropertyName default'u + parent süzgeci).</summary>
    [Parameter] public string EntityName { get; set; } = string.Empty;
    [Parameter] public string PropertyName { get; set; } = string.Empty;

    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    private ICommitCoordinator<SpecialCodeGetDto, SpecialCodeListDto, Guid, SpecialCodeListRequestDto>? _coordinator;
    private List<SpecialCodeListDto> _parentOptions = new();
    private bool _ready;

    protected override async Task OnInitializedAsync()
    {
        _coordinator = new PersistentCoordinator<SpecialCodeGetDto, SpecialCodeListDto, Guid, SpecialCodeListRequestDto, SpecialCodeCreateDto, SpecialCodeUpdateDto>(
            SpecialCodeAppService, Mapper);

        await Working.EnsureLoadedAsync();

        // Aynı bağlamdaki kodlar parent adayı; layout kendini (Model.Id) hariç tutar.
        _parentOptions = await SpecialCodeAppService.GetForContextAsync(EntityName, PropertyName);

        _ready = true;
    }
}
