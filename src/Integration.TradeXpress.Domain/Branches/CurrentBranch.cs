using System.Threading;

namespace Integration.TradeXpress.Branches;

/// <summary>
/// <see cref="ICurrentBranch"/> uygulaması (<c>CurrentCompany</c> deseninin birebir aynası). Değer önceliği:
/// <see cref="Change"/> override (AsyncLocal) → yoksa <see cref="IBranchContextProvider"/> (Blazor
/// working-context sağlayıcısı / API'de null).
/// </summary>
public class CurrentBranch : ICurrentBranch, ITransientDependency
{
    private static readonly AsyncLocal<BranchOverride?> _override = new();

    private readonly IBranchContextProvider _provider;

    public CurrentBranch(IBranchContextProvider provider)
    {
        _provider = provider;
    }

    public Guid? Id => _override.Value is { } o ? o.BranchId : _provider.GetCurrentBranchId();

    public IDisposable Change(Guid? branchId)
    {
        var previous = _override.Value;
        _override.Value = new BranchOverride(branchId);
        return new DisposeAction(() => _override.Value = previous);
    }

    private sealed record BranchOverride(Guid? BranchId);
}

/// <summary>Varsayılan kaynak (host / HTTP API): aktif şube yok → null. Blazor'daki
/// <see cref="IBranchContextProvider"/> uygulaması bunu değiştirir.</summary>
public class NullBranchContextProvider : IBranchContextProvider, ISingletonDependency
{
    public Guid? GetCurrentBranchId()
    {
        return null;
    }
}
