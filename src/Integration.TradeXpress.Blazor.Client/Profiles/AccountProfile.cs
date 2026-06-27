using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Accounts;
using Integration.TradeXpress.Accounts;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Hesap (Account) entity profili — SubAccount'un parent'ı (Account edit formunda "Alt Hesaplar" drill'i).
/// Kimlik (ikon/başlık/permission/edit-host) TEK KAYNAK.
/// </summary>
public sealed class AccountProfile : EntityProfile<AccountGetDto, AccountListDto, Guid>
{
    public override string Key => "Account";
    public override string IconCssClass => TradeXpressIcons.Account;
    public override string CaptionKey => "Account";
    public override string PluralCaptionKey => "Accounts";
    public override string? PermissionPrefix => TradeXpressPermissions.Accounts.Default;
    public override Type EditComponentType => typeof(AccountEditHost);
    public override string? RouteBase => "/accounts";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
