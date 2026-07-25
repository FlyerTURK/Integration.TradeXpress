using System.Linq;
using System.Linq.Expressions;
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
    /// <summary>Görünürlük kuralının expression hâli — HostCatalogCrudAppService predicate override'ları için.</summary>
    public static Expression<Func<T, bool>> CompanyVisiblePredicate<T>(Guid? tenantId, Guid? companyId)
        where T : class, IMultiTenant, ICompanyScoped
    {
        return x =>
            x.TenantId == null
            || (x.TenantId == tenantId
                && (companyId == null || x.CompanyId == null || x.CompanyId == companyId));
    }

    public static IQueryable<T> WhereCompanyVisible<T>(this IQueryable<T> query, Guid? tenantId, Guid? companyId)
        where T : class, IMultiTenant, ICompanyScoped
    {
        return query.Where(CompanyVisiblePredicate<T>(tenantId, companyId));
    }

    /// <summary>Aynı görünürlük kuralının <see cref="ICompanyOwned"/> (GÜVENLİK SINIRI) karşılığı — emtia aileleri
    /// buraya taşındı. <b>Fark:</b> "CompanyId == null → herkese görünür" kolu YOKTUR; sahipsiz kayıt zaten
    /// üretilemez (CompanyId zorunlu) ve holding kaçağı yapısal olarak imkânsızdır. Host kaydı (TenantId=null)
    /// muafiyeti ve "şirket seçili değilken konsolide" davranışı KORUNUR (kardeş kuralla hizalı).
    /// <para>Global sorgu filtresi bunu zaten uygular; bu yardımcı, filtrenin BİLİNÇLİ kapatıldığı okuma
    /// kapsamlarında (ör. host katalog zenginleştirmesi) görünürlüğü yeniden kurmak içindir.</para></summary>
    public static Expression<Func<T, bool>> CompanyOwnedVisiblePredicate<T>(Guid? tenantId, Guid? companyId)
        where T : class, IMultiTenant, ICompanyOwned
    {
        return x =>
            x.TenantId == null
            || (x.TenantId == tenantId && (companyId == null || x.CompanyId == companyId));
    }

    public static IQueryable<T> WhereCompanyOwnedVisible<T>(this IQueryable<T> query, Guid? tenantId, Guid? companyId)
        where T : class, IMultiTenant, ICompanyOwned
    {
        return query.Where(CompanyOwnedVisiblePredicate<T>(tenantId, companyId));
    }
}
