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

    public override System.Type EditComponentType => typeof(AccountEditHost);

    void IDisposable.Dispose()
    {
        Working.Changed -= OnWorkingChanged;
        base.Dispose();
    }
}
