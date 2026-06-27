using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Admin;
using Integration.TradeXpress.Blazor.Client.Pages.Admin.Models;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Kullanıcı (User) entity profili — backend ABP <c>IIdentityUserAppService</c> (adapter DTO'ları). UI kimliği
/// (ikon/başlık) TEK KAYNAK; tenant onboarding "Kullanıcılar" sekmesi + standalone user list/edit tüketir.
/// İzin (permission) ABP Identity tarafında yönetilir → PermissionPrefix YOK.
/// </summary>
public sealed class UserProfile : EntityProfile<UserGetDto, UserListDto, Guid>
{
    public override string Key => "User";
    public override string IconCssClass => TradeXpressIcons.User;
    public override string CaptionKey => "Entity:User";
    public override string PluralCaptionKey => "Users";
    public override Type EditComponentType => typeof(UserEditHost);
    public override string? RouteBase => "/admin/users";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
