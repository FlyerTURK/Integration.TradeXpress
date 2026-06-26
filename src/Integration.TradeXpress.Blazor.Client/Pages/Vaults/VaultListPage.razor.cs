using System;
using Integration.TradeXpress.Vaults;
using Integration.TradeXpress.Permissions;
using Microsoft.AspNetCore.Components;

namespace Integration.TradeXpress.Blazor.Client.Pages.Vaults;

public partial class VaultListPage
{
    public VaultListPage()
    {
        LocalizationResource = typeof(Integration.TradeXpress.Localization.TradeXpressResource);
    }

    [Parameter]
    public Guid BranchId { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "branchcode")]
    public string? BranchCode { get; set; }

    [Inject]
    protected IVaultAppService VaultAppService { get; set; } = default!;

    public override Volo.Abp.Application.Services.ICrudAppService<
        VaultGetDto, VaultListDto, Guid,
        VaultListRequestDto, VaultCreateDto, VaultUpdateDto> CrudAppService
        => VaultAppService;

    protected override string EditTitle => string.IsNullOrWhiteSpace(BranchCode) ? base.EditTitle : $"{base.EditTitle} - [{L["Entity:Branch"]}: {BranchCode}]";
    protected override string PermissionPrefix => TradeXpressPermissions.Vaults.Default;

    private string PageTitle => string.IsNullOrWhiteSpace(BranchCode)
        ? L["Menu:Vaults"]
        : $"{L["Menu:Vaults"]} - [{L["Entity:Branch"]}: {BranchCode}]";

    // Drill-down: yalnız bu şubeye ait kasalar.
    protected override void OnConfiguringListRequest(VaultListRequestDto request)
        => request.BranchId = BranchId;

    // PİLOT: yeni mimari edit (agnostic EntityEditForm + PersistentCoordinator). Eski VaultEditPage repo'da kalır.
    public override System.Type EditComponentType => typeof(VaultEditHost);

    // Drill-down bağlamı: yeni kasanın şubesi (Id boş-guid bug'ı düzeltildi) + şube kodu (header L3: "Şube: MRK").
    protected override System.Collections.Generic.Dictionary<string, object>? AdditionalEditParameters
        => new() { ["BranchId"] = BranchId, ["BranchCode"] = BranchCode ?? string.Empty };
}





