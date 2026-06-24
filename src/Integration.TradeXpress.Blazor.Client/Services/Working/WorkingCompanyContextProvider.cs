using System;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// <see cref="ICompanyContextProvider"/> köprüsü — Blazor circuit'indeki <see cref="IWorkingContextService"/>'in
/// çalışılan şirketini sunucu-ambient <see cref="ICurrentCompany"/>'ye taşır. Böylece AppService/DbContext
/// aktif şirketi DTO'dan değil ambient'ten okur (auto-stamp + merkezi filtre bununla çalışır).
/// Default <c>NullCompanyContextProvider</c>'ı değiştirir.
/// </summary>
[Dependency(ReplaceServices = true)]
public class WorkingCompanyContextProvider : ICompanyContextProvider, IScopedDependency
{
    private readonly IWorkingContextService _working;

    public WorkingCompanyContextProvider(IWorkingContextService working)
    {
        _working = working;
    }

    public Guid? GetCurrentCompanyId() => _working.CurrentCompanyId;
}
