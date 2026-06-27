using System;
using Integration.Framework.Blazor.Client.Profiles;
using Integration.TradeXpress.Blazor.Client.Pages.Companies;
using Integration.TradeXpress.Companies;
using Integration.TradeXpress.Permissions;

namespace Integration.TradeXpress.Blazor.Client.Profiles;

/// <summary>
/// Şirket (Company) entity profili — org hiyerarşisinin KÖKÜ (Branch'in parent'ı). Kimlik (ikon/başlık/
/// permission/edit-host) TEK KAYNAK; tenant onboarding "Şirketler" sekmesi + standalone Company list/edit tüketir.
/// </summary>
public sealed class CompanyProfile : EntityProfile<CompanyGetDto, CompanyListDto, Guid>
{
    public override string Key => "Company";
    public override string IconCssClass => TradeXpressIcons.Company;
    public override string CaptionKey => "Entity:Company";
    public override string PluralCaptionKey => "Companies";
    public override string? PermissionPrefix => TradeXpressPermissions.Companies.Default;
    public override Type EditComponentType => typeof(CompanyEditHost);
    public override string? RouteBase => "/companies";
    public override EntityPersistence Persistence => EntityPersistence.Persistent;
}
