using System.Threading;

namespace Integration.TradeXpress.Vaults;

/// <summary>
/// <see cref="ICurrentVault"/> uygulaması (<c>CurrentCompany</c> deseninin birebir aynası). Değer önceliği:
/// <see cref="Change"/> override (AsyncLocal) → yoksa <see cref="IVaultContextProvider"/> (Blazor
/// working-context sağlayıcısı / API'de null).
///
/// <para>Hatırlatma: bu değer hiçbir query-filter'a bağlanmaz (ortam varsayılanı, kısıtlama değil).</para>
/// </summary>
public class CurrentVault : ICurrentVault, ITransientDependency
{
    private static readonly AsyncLocal<VaultOverride?> _override = new();

    private readonly IVaultContextProvider _provider;

    public CurrentVault(IVaultContextProvider provider)
    {
        _provider = provider;
    }

    public Guid? Id => _override.Value is { } o ? o.VaultId : _provider.GetCurrentVaultId();

    public IDisposable Change(Guid? vaultId)
    {
        var previous = _override.Value;
        _override.Value = new VaultOverride(vaultId);
        return new DisposeAction(() => _override.Value = previous);
    }

    private sealed record VaultOverride(Guid? VaultId);
}

/// <summary>Varsayılan kaynak (host / HTTP API): aktif kasa yok → null. Blazor'daki
/// <see cref="IVaultContextProvider"/> uygulaması bunu değiştirir.</summary>
public class NullVaultContextProvider : IVaultContextProvider, ISingletonDependency
{
    public Guid? GetCurrentVaultId()
    {
        return null;
    }
}
