using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Companies;
using Integration.TradeXpress.Branches;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Şube (Branch) entity profili — Faz 1'de öncelikle Vault'un PARENT kimliğini (başlık/ikon) tek kaynaktan
/// sağlamak için (Vault.ParentProfileKey = "Branch"). Tam tüketim (Branch list/edit) sonraki fazda.
/// </summary>
public sealed class BranchProfile : EntityProfile<BranchGetDto, BranchListDto, Guid>
{
    public override string Key => "Branch";
    public override string IconCssClass => TradeXpressIcons.Branch;
    public override string CaptionKey => "Entity:Branch";
    public override string PluralCaptionKey => "Menu:Branches";
    public override string? PermissionPrefix => TradeXpressPermissions.Branches.Default;
    public override string? ParentProfileKey => "Company";
    public override Type EditComponentType => typeof(BranchEditHost);
    public override string? RouteBase => "/branches";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
