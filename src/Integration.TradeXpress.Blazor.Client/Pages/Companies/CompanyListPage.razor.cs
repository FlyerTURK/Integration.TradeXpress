using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.TradeXpress.Blazor.Client.Pages.Companies.Models;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

public partial class CompanyListPage
{
    public CompanyListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Inject] protected ICompanyAppService CompanyAppService { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        CompanyGetDto, CompanyListDto, Guid,
        CompanyListRequestDto, CompanyCreateDto, CompanyUpdateDto> CrudAppService
        => CompanyAppService;

    protected override string PermissionPrefix => TradeXpressPermissions.Companies.Default;

    // ── Toolbar drill (persistent): Şirket → Şubeler → Kasalar ───────────────────

    private CompanyListDto? SelectedCompany =>
        StateService.SelectedDataItems is { Count: 1 } sel ? sel[0] as CompanyListDto : null;

    private string SelectedCompanyName => SelectedCompany?.Name ?? string.Empty;

    // Drill-down artık URL navigasyonu değil — şubeleri MDI sekmesi olarak açar/aktive eder.
    private async Task OpenBranchesAsync()
    {
        if (SelectedCompany is null) return;
        var url = $"/branches/{SelectedCompany.Id}?companycode={Uri.EscapeDataString(SelectedCompany.Code)}";
        var title = $"{L["Menu:Branches"]} - [{L["Entity:Company"]}: {SelectedCompany.Code}]";
        await TabManager.OpenOrActivateAsync(url, title, TradeXpressIcons.Branch);
    }



        public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Companies.CompanyEditPage);
    }


