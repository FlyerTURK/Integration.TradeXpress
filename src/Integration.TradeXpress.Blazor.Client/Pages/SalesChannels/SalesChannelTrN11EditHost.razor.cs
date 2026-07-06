using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>N11 satış kanalı edit host code-behind — coordinator kurulumu (tipe-özel ISalesChannelTrN11AppService).</summary>
public partial class SalesChannelTrN11EditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISalesChannelTrN11AppService AppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<SalesChannelTrN11GetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto>? _coordinator;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<SalesChannelTrN11GetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto, SalesChannelTrN11CreateDto, SalesChannelTrN11UpdateDto>(
            AppService, Mapper);
    }
}
