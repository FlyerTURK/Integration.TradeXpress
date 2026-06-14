using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace Integration.TradeXpress.Tenants;

/// <summary>
/// Yeni tenant oluşturulduğunda yayınlanacak <see cref="TenantCreatedEto"/>'yu,
/// admin kullanıcı seed'i için gereken property'lerle birlikte kurar.
/// </summary>
public static class TenantCreatedEtoFactory
{
    public static TenantCreatedEto Create(Tenant tenant, string adminEmailAddress, string adminPassword)
    {
        var eto = new TenantCreatedEto
        {
            Id = tenant.Id,
            Name = tenant.Name
        };

        eto.Properties[IdentityDataSeedContributor.AdminEmailPropertyName] = adminEmailAddress;
        eto.Properties[IdentityDataSeedContributor.AdminPasswordPropertyName] = adminPassword;

        return eto;
    }
}
