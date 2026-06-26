using System;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Accounts;

public partial class SubAccountListPage
    : CrudPageBase<SubAccountGetDto, SubAccountListDto, Guid, SubAccountListRequestDto, SubAccountCreateDto, SubAccountUpdateDto>
{
    public SubAccountListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Parameter]
    public Guid AccountId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "accountcode")]
    public string? AccountCode { get; set; }

    [Inject] protected ISubAccountAppService SubAccountAppService { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        SubAccountGetDto, SubAccountListDto, Guid,
        SubAccountListRequestDto, SubAccountCreateDto, SubAccountUpdateDto> CrudAppService
        => SubAccountAppService;

    protected override string EditTitle => string.IsNullOrWhiteSpace(AccountCode) ? base.EditTitle : $"{base.EditTitle} - [{L["Entity:Account"]}: {AccountCode}]";
    protected override string PermissionPrefix => TradeXpressPermissions.Accounts.Default;

    private string PageTitle => string.IsNullOrWhiteSpace(AccountCode)
        ? L["SubAccounts"]
        : $"{L["SubAccounts"]} - [{L["Account"]}: {AccountCode}]";

    protected override void OnConfiguringListRequest(SubAccountListRequestDto request)
        => request.AccountId = AccountId;

    /// <summary>AccountCode linki → hesabın edit'ini MDI sekmesinde aç.</summary>
    private async Task OpenAccountAsync(SubAccountListDto s)
    {
        if (s.AccountId == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/accounts/{s.AccountId}",
            $"{L["Account"]}: {s.AccountCode}",
            TradeXpressIcons.Account);
    }

    // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator
    public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Accounts.SubAccountEditHost);

    // Yeni alt hesaba parent hesabı (route'tan gelen AccountId) geçir → SubAccountEditHost.AccountId (popup param).
    protected override System.Collections.Generic.Dictionary<string, object>? AdditionalEditParameters
        => new() { ["AccountId"] = AccountId };
}


