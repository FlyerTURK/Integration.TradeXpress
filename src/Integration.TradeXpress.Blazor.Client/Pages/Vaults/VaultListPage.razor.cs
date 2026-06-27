using System;
using Integration.TradeXpress.Vaults;
using Integration.Framework.Blazor.Client.Profiles;
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

    [Inject]
    protected IEntityProfileRegistry Profiles { get; set; } = default!;

    /// <summary>Bu sayfanın entity KİMLİĞİ tek kaynak: ikon/başlık/permission/edit-host profilden gelir.</summary>
    private EntityProfile? _profile;
    protected EntityProfile Profile => _profile ??= Profiles.Get(typeof(VaultListDto));

    /// <summary>Parent (Branch) kimliği — başlık/etiketler için profilden (Vault.ParentProfileKey).</summary>
    private EntityProfile? _parentProfile;
    protected EntityProfile ParentProfile => _parentProfile ??= Profiles.GetByKey(Profile.ParentProfileKey!);

    public override Volo.Abp.Application.Services.ICrudAppService<
        VaultGetDto, VaultListDto, Guid,
        VaultListRequestDto, VaultCreateDto, VaultUpdateDto> CrudAppService
        => VaultAppService;

    protected override string EditTitle => string.IsNullOrWhiteSpace(BranchCode) ? base.EditTitle : $"{base.EditTitle} - [{L[ParentProfile.CaptionKey]}: {BranchCode}]";
    protected override string? PermissionPrefix => Profile.PermissionPrefix;
    protected override string? EditIconCssClass => Profile.IconCssClass;

    private string PageTitle => string.IsNullOrWhiteSpace(BranchCode)
        ? L[Profile.PluralCaptionKey]
        : $"{L[Profile.PluralCaptionKey]} - [{L[ParentProfile.CaptionKey]}: {BranchCode}]";

    // Drill-down: yalnız bu şubeye ait kasalar.
    protected override void OnConfiguringListRequest(VaultListRequestDto request)
        => request.BranchId = BranchId;

    // PİLOT: yeni mimari edit (agnostic EntityEditForm + PersistentCoordinator). Edit host TİPİ profilden.
    public override System.Type EditComponentType => Profile.EditComponentType;

    // Drill-down bağlamı: yeni kasanın şubesi (Id boş-guid bug'ı düzeltildi) + şube kodu (header L3: "Şube: MRK").
    protected override System.Collections.Generic.Dictionary<string, object>? AdditionalEditParameters
        => new() { ["BranchId"] = BranchId, ["BranchCode"] = BranchCode ?? string.Empty };
}





