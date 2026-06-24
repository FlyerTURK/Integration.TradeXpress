using System;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Company-scoped kayıt işaretçisi. <c>CompanyId == null</c> = holding-host (tüm şirketlere açık) /
/// host global (TenantId=null ise). Dolu = o şirkete-özel. Tenant'ın bir alt katı: tenant nasıl host/holding
/// ayrımıysa, CompanyId de holding-host/şirket ayrımıdır. <b>Güvenlik sınırı DEĞİL</b> (o tenant); görünüm filtresi.
/// </summary>
public interface ICompanyScoped
{
    Guid? CompanyId { get; }
}
