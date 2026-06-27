using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Vaults;
using Integration.TradeXpress.Permissions;
using Integration.TradeXpress.Vaults;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Kasa (Vault) entity profili — kimlik TEK KAYNAK (Faz 1 pilot). İkon/başlık/permission/edit-host/parent
/// burada tanımlanır; <see cref="Pages.Vaults.VaultListPage"/> + <see cref="VaultEditHost"/> bunu tüketir.
/// Persistent: standalone tam-sayfa liste VEYA şube içinde persistent drill olarak AYNI makineyle çalışır.
/// </summary>
public sealed class VaultProfile : EntityProfile<VaultGetDto, VaultListDto, Guid>
{
    public override string Key => "Vault";
    public override string IconCssClass => TradeXpressIcons.Vault;
    public override string CaptionKey => "Entity:Vault";
    public override string PluralCaptionKey => "Menu:Vaults";
    public override string? PermissionPrefix => TradeXpressPermissions.Vaults.Default;
    public override string? ParentProfileKey => "Branch";
    public override Type EditComponentType => typeof(VaultEditHost);
    public override string? RouteBase => "/vaults";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
