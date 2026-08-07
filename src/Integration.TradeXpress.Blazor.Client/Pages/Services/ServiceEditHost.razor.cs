using System;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.Services;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Services;

/// <summary>Hizmet edit host — ince sarmal (coordinator + config; global Service tenant'ta salt-okunur).
/// (@code bloğu 2026-08-07'de code-behind'a taşındı — dosyaya dokunma kuralı.)</summary>
public partial class ServiceEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>ÇAĞRI-BAŞI footer daraltma (2026-08-06 Hakan kararı) — gerekçe GoodEditHost'ta.</summary>
    [Parameter] public bool SupportsSaveAndNew { get; set; } = true;

    [Parameter] public bool SupportsDelete { get; set; } = true;

    private ICommitCoordinator<ServiceGetDto, ServiceListDto, Guid, ServiceListRequestDto>? _coordinator;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<ServiceGetDto, ServiceListDto, Guid, ServiceListRequestDto, ServiceCreateDto, ServiceUpdateDto>(
            ServiceAppService, Mapper);
    }
}
