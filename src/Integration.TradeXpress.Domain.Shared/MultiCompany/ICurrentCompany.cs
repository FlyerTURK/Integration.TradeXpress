using System;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// Aktif (çalışılan) şirket — ABP'nin <c>ICurrentTenant</c>'ının company eşdeğeri. Değer
/// <see cref="ICompanyContextProvider"/>'dan gelir (Blazor'da working-context köprüsü); sunucu işlemleri
/// <see cref="Change"/> ile geçici override edebilir (seed / cross-company).
/// </summary>
public interface ICurrentCompany
{
    Guid? Id { get; }

    /// <summary>Geçici şirket override scope'u (using ile geri alınır).</summary>
    IDisposable Change(Guid? companyId);
}

/// <summary>
/// Aktif şirketin kaynağı. Varsayılan (host/API): null. Blazor circuit'inde working-context'e köprülenir.
/// </summary>
public interface ICompanyContextProvider
{
    Guid? GetCurrentCompanyId();
}
