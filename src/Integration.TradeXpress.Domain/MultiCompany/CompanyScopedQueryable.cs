using System.Linq;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.MultiTenancy;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Company-scoped kataloglar için merkezi görünürlük filtresi (tek yerde — her AppService'te elle Where yazılmaz):
/// host (TenantId=null) + (kendi tenant'ı &amp;&amp; (holding-host CompanyId=null || çalışılan şirket)).
/// <c>companyId == null</c> (şirket seçili değil) → konsolide: tenant'ın tüm şirketleri görünür.
/// </summary>
public static class CompanyScopedQueryable
{
    public static IQueryable<T> WhereCompanyVisible<T>(this IQueryable<T> query, Guid? tenantId, Guid? companyId)
        where T : class, IMultiTenant, ICompanyScoped
    {
        return query.Where(x =>
            x.TenantId == null
            || (x.TenantId == tenantId
                && (companyId == null || x.CompanyId == null || x.CompanyId == companyId)));
    }
}
