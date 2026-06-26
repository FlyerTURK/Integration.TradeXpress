using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Accounts;

public partial class AccountListPage : IDisposable
{
    public AccountListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject] protected IAccountAppService AccountAppService { get; set; } = default!;
    [Inject] protected IWorkingContextService Working { get; set; } = default!;
    [Inject] protected Integration.TradeXpress.Blazor.Client.Services.Mdi.ITabManager TabManager { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        Working.Changed += OnWorkingChanged;
        await Working.EnsureLoadedAsync();
    }

    // Çalışma şirketi değişince hesap listesi yeni şirkete göre yenilensin.
    private void OnWorkingChanged() => _ = InvokeAsync(async () => { await GetListAsync(); StateHasChanged(); });

    // Company-scoped: yalnız çalışma şirketinin hesapları.
    protected override void OnConfiguringListRequest(AccountListRequestDto request)
        => request.CompanyId = Working.CurrentCompanyId;

    public override Volo.Abp.Application.Services.ICrudAppService<
        AccountGetDto, AccountListDto, Guid,
        AccountListRequestDto, AccountCreateDto, AccountUpdateDto> CrudAppService => AccountAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Accounts.Default;

    private AccountListDto? SelectedAccount =>
        StateService.SelectedDataItems is { Count: 1 } sel ? sel[0] as AccountListDto : null;

    private System.Collections.Generic.IReadOnlyList<Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction> SubAccountActions => new[]
    {
        new Integration.Framework.Blazor.Client.Components.Crud.CrudToolbarAction
        {
            SortIndex = 300,
            Text = L["SubAccounts"],
            Tooltip = L["SubAccounts"],
            IconCssClass = $"{TradeXpressIcons.SubAccount} toolbar-action-subaccounts",
            Enabled = SelectedAccount != null,
            OnClick = OpenSubAccountsAsync,
        },
    };

    private async Task OpenSubAccountsAsync()
    {
        if (SelectedAccount is null) return;
        var url = $"/subaccounts/{SelectedAccount.Id}?accountcode={Uri.EscapeDataString(SelectedAccount.Code)}";
        var header = new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData {
            FormCaption = L["SubAccounts"],
            IconCssClass = TradeXpressIcons.SubAccount,
            ParentLabel = L["Account"],
            ParentValue = SelectedAccount.Code
        };
        await TabManager.OpenOrActivateAsync(url, header);
    }

    public override System.Type EditComponentType => typeof(AccountEditHost);

    void IDisposable.Dispose()
    {
        Working.Changed -= OnWorkingChanged;
        base.Dispose();
    }
}
