using System;
using Integration.TradeXpress.Vaults;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// <see cref="IVaultContextProvider"/> uygulaması — circuit'teki çalışma KASASINI sunucu-ambient
/// <see cref="ICurrentVault"/>'a taşır (<c>WorkingCompanyContextProvider</c> deseniyle BİREBİR aynı).
/// Default <c>NullVaultContextProvider</c>'ı değiştirir.
///
/// <para><b>MAYIN — kaynak singleton store'dur:</b> scoped <c>IWorkingContextService</c>'ten OKUNMAZ (ABP UoW
/// child scope'unda boş kopya çözülür — ölçümle kanıtlı). Bkz. <c>WorkingCompanyContextProvider:49-56</c>.</para>
///
/// <para><b>Kısıtlama DEĞİL:</b> bu değer hiçbir global query-filter'a bağlanmaz — kullanıcı birden çok kasaya
/// hâkim olabilmelidir. Ambient yalnız ortam varsayılanını taşır; formların kasa seçicisi kalır.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
public class WorkingVaultContextProvider : IVaultContextProvider, IScopedDependency
{
    private readonly WorkingSelectionStore _selectionStore;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public WorkingVaultContextProvider(
        WorkingSelectionStore selectionStore,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _selectionStore = selectionStore;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public Guid? GetCurrentVaultId()
    {
        var entry = _currentUser.Id is { } userId
            ? _selectionStore.Get(_currentTenant.Id, userId)
            : null;

        return entry?.SelectedVaultId;
    }
}
