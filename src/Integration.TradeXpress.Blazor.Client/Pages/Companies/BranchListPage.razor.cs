using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Integration.Framework.Blazor.Client.Components.Crud;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Services.Mdi;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Companies;

public partial class BranchListPage
    : CrudPageBase<BranchGetDto, BranchListDto, Guid, BranchListRequestDto, BranchCreateDto, BranchUpdateDto>
{
    public BranchListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Parameter]
    public Guid CompanyId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "companycode")]
    public string? CompanyCode { get; set; }

    [Inject] protected IBranchAppService BranchAppService { get; set; } = default!;
    [Inject] protected ITabManager TabManager { get; set; } = default!;
    [Inject] protected IEntityProfileRegistry Profiles { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        BranchGetDto, BranchListDto, Guid,
        BranchListRequestDto, BranchCreateDto, BranchUpdateDto> CrudAppService
        => BranchAppService;

    protected override string EditTitle => string.IsNullOrWhiteSpace(CompanyCode) ? base.EditTitle : $"{base.EditTitle} - [{L["Entity:Company"]}: {CompanyCode}]";
    protected override string PermissionPrefix => TradeXpressPermissions.Branches.Default;

    private string PageTitle => string.IsNullOrWhiteSpace(CompanyCode)
        ? L["Menu:Branches"]
        : $"{L["Menu:Branches"]} - [{L["Entity:Company"]}: {CompanyCode}]";

    protected override void OnConfiguringListRequest(BranchListRequestDto request)
        => request.CompanyId = CompanyId;

    /// <summary>CompanyCode linki → şirketin edit'ini MDI sekmesinde aç.</summary>
    private async Task OpenCompanyAsync(BranchListDto b)
    {
        if (b.CompanyId == Guid.Empty) return;
        await TabManager.OpenOrActivateAsync(
            $"/companies/{b.CompanyId}",
            $"{L["Entity:Company"]}: {b.CompanyCode}",
            TradeXpressIcons.Company);
    }

    // ── Toolbar drill: Şube → Kasalar ───────────────────────────────────────────
    private BranchListDto? SelectedBranch =>
        StateService.SelectedDataItems is { Count: 1 } sel ? sel[0] as BranchListDto : null;

    /// <summary>Toolbar custom action — "Kasalar" drill (SortIndex 300: Sil ile Arama arası).</summary>
    private IReadOnlyList<CrudToolbarAction> VaultActions => new[]
    {
        new CrudToolbarAction
        {
            SortIndex = 300,
            Text = L["Menu:Vaults"],
            Tooltip = L["Menu:Vaults"],
            IconCssClass = $"{TradeXpressIcons.Vault} toolbar-action-vaults",
            Enabled = SelectedBranch != null,
            OnClick = OpenVaultsAsync,
        },
    };

    // Drill-down artık URL navigasyonu değil — kasaları MDI sekmesi olarak açar/aktive eder.
    private async Task OpenVaultsAsync()
    {
        if (SelectedBranch is null) return;
        var url = $"/vaults/{SelectedBranch.Id}?branchcode={Uri.EscapeDataString(SelectedBranch.Code)}";
        // Tab ikonu TEK KAYNAK = VaultProfile (popup ile birebir aynı; "Vault" = entity profil anahtarı).
        var header = new Integration.Framework.Blazor.Client.Services.Mdi.TabHeaderData {
            FormCaption = L["Menu:Vaults"],
            IconCssClass = Profiles.GetByKey("Vault").IconCssClass,
            ParentLabel = L["Entity:Branch"],
            ParentValue = SelectedBranch.Code
        };
        await TabManager.OpenOrActivateAsync(url, header);
    }



        // YENİ mimari: agnostic EntityEditForm + PersistentCoordinator (eski BranchEditPage kaldırıldı).
        public override System.Type EditComponentType => typeof(Integration.TradeXpress.Blazor.Client.Pages.Companies.BranchEditHost);

        // Yeni branch'e parent şirketi (route'tan gelen CompanyId) geçir → BranchEditHost.CompanyId (popup param).
        protected override System.Collections.Generic.Dictionary<string, object>? AdditionalEditParameters
            => new() { ["CompanyId"] = CompanyId };
    }






