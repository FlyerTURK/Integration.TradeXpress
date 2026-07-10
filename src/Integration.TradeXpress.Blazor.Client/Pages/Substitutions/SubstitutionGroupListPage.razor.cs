using System;
using System.Threading.Tasks;
using Integration.TradeXpress.Blazor.Client.Services.Working;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Substitutions;
using Microsoft.AspNetCore.Components;
using Volo.Abp.Application.Services;

namespace Integration.TradeXpress.Blazor.Client.Pages.Substitutions;

public partial class SubstitutionGroupListPage : IDisposable
{
    public SubstitutionGroupListPage()
    {
        LocalizationResource = typeof(TradeXpressResource);
    }

    [Inject] protected ISubstitutionGroupAppService SubstitutionGroupAppService { get; set; } = default!;
    [Inject] protected IWorkingContextService Working { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        Working.Changed += OnWorkingChanged;
        await Working.EnsureLoadedAsync();
    }

    // Çalışma şirketi değişince liste yeni şirkete göre yenilensin (kayıtlar company-owned;
    // scope'u sunucudaki ICompanyOwned query-filter verir, client CompanyId göndermez).
    private void OnWorkingChanged()
    {
        _ = InvokeAsync(async () =>
        {
            await GetListAsync();
            StateHasChanged();
        });
    }

    public override ICrudAppService<
        SubstitutionGroupGetDto, SubstitutionGroupListDto, Guid,
        SubstitutionGroupListRequestDto, SubstitutionGroupCreateDto, SubstitutionGroupUpdateDto> CrudAppService
        => SubstitutionGroupAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Substitutions.Default;

    public override Type EditComponentType => typeof(SubstitutionGroupEditHost);

    void IDisposable.Dispose()
    {
        Working.Changed -= OnWorkingChanged;
        base.Dispose();
    }
}
