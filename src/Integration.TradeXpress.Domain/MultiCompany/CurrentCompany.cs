using System;
using System.Threading;
using Integration.TradeXpress.MultiCompany;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.MultiCompany;

/// <summary>
/// <see cref="ICurrentCompany"/> uygulaması. Değer önceliği: <see cref="Change"/> override (AsyncLocal) →
/// yoksa <see cref="ICompanyContextProvider"/> (Blazor working-context köprüsü / API'de null).
/// </summary>
public class CurrentCompany : ICurrentCompany, ITransientDependency
{
    private static readonly AsyncLocal<CompanyOverride?> _override = new();

    private readonly ICompanyContextProvider _provider;

    public CurrentCompany(ICompanyContextProvider provider)
    {
        _provider = provider;
    }

    public Guid? Id => _override.Value is { } o ? o.CompanyId : _provider.GetCurrentCompanyId();

    public IDisposable Change(Guid? companyId)
    {
        var previous = _override.Value;
        _override.Value = new CompanyOverride(companyId);
        return new DisposeAction(() => _override.Value = previous);
    }

    private sealed record CompanyOverride(Guid? CompanyId);
}

/// <summary>Varsayılan kaynak (host / HTTP API): aktif şirket yok → null. Blazor köprüsü bunu değiştirir.</summary>
public class NullCompanyContextProvider : ICompanyContextProvider, ISingletonDependency
{
    public Guid? GetCurrentCompanyId() => null;
}
