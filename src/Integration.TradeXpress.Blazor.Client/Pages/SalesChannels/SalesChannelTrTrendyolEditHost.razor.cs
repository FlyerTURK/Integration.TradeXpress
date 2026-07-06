using System;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Trendyol satış kanalı edit host code-behind — coordinator kurulumu (tipe-özel ISalesChannelTrTrendyolAppService).</summary>
public partial class SalesChannelTrTrendyolEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISalesChannelTrTrendyolAppService AppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;

    private ICommitCoordinator<SalesChannelTrTrendyolGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto>? _coordinator;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<SalesChannelTrTrendyolGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto, SalesChannelTrTrendyolCreateDto, SalesChannelTrTrendyolUpdateDto>(
            AppService, Mapper);
    }
}
