using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Accounts;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Alt Hesap (SubAccount) entity profili — Account'un in-memory drill çocuğu ("Alt Hesaplar" sekmesi).
/// Kimlik (ikon/başlık/permission/edit-host) TEK KAYNAK; parent = Account.
/// </summary>
public sealed class SubAccountProfile : EntityProfile<SubAccountGetDto, SubAccountListDto, Guid>
{
    public override string Key => "SubAccount";
    public override string IconCssClass => TradeXpressIcons.SubAccount;
    public override string CaptionKey => "SubAccount";
    public override string PluralCaptionKey => "SubAccounts";
    public override string? PermissionPrefix => TradeXpressPermissions.SubAccounts.Default;
    public override string? ParentProfileKey => "Account";
    public override Type EditComponentType => typeof(SubAccountEditHost);
    public override string? RouteBase => "/subaccounts";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
