using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Services.Base;
using Integration.TradeXpress.SalesChannels;
using Microsoft.AspNetCore.Components;
using Volo.Abp.ObjectMapping;

namespace Integration.TradeXpress.Blazor.Client.Pages.SalesChannels;

/// <summary>Etsy satış kanalı edit host code-behind — coordinator kurulumu (tipe-özel ISalesChannelEtsyAppService)
/// + OAuth köprüsü ("Bağlan" → authorize URL'e tam yönlendirme; callback dönüşünde ?oauth=ok|err toast'u).</summary>
public partial class SalesChannelEtsyEditHost
{
    [Parameter] public Guid? Id { get; set; }
    [Parameter] public bool IsPopupMode { get; set; }
    [Parameter] public EventCallback OnSaved { get; set; }
    [Parameter] public EventCallback OnClosed { get; set; }

    [Inject] protected ISalesChannelEtsyAppService AppService { get; set; } = default!;
    [Inject] protected IObjectMapper Mapper { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected IUiInteractionService UiService { get; set; } = default!;

    private ICommitCoordinator<SalesChannelEtsyGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto>? _coordinator;

    protected override void OnInitialized()
    {
        _coordinator = new PersistentCoordinator<SalesChannelEtsyGetDto, SalesChannelListDto, Guid, SalesChannelListRequestDto, SalesChannelEtsyCreateDto, SalesChannelEtsyUpdateDto>(
            AppService, Mapper);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // OAuth callback dönüşü: endpoint bu sayfaya ?oauth=ok|err ile yönlendirir → kullanıcıya sonucu söyle.
        // (Teknik detay server logunda; kullanıcıya dostane özet yeter.)
        var query = new Uri(Navigation.Uri).Query;
        if (query.Contains("oauth=ok", StringComparison.Ordinal))
        {
            UiService.ShowSuccessToast(L["SalesChannel:Etsy:OAuthSucceeded"]);
        }
        else if (query.Contains("oauth=err", StringComparison.Ordinal))
        {
            UiService.ShowErrorToast(L["SalesChannel:Etsy:OAuthFailed"]);
        }
    }

    /// <summary>"Etsy'ye Bağlan": sunucu PKCE state/verifier üretip authorize URL döner → TAM yönlendirme
    /// (forceLoad — Etsy harici sayfa; circuit'ten çıkılır, dönüş callback endpoint üzerinden olur).</summary>
    private async Task StartOAuthAsync(SalesChannelEtsyGetDto model)
    {
        try
        {
            var url = await AppService.StartOAuthAsync(model.Id);
            Navigation.NavigateTo(url, forceLoad: true);
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
    }
}
