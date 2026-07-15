using System;
using Integration.TradeXpress.Branches;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// <see cref="IBranchContextProvider"/> köprüsü — circuit'teki çalışma ŞUBESİNİ sunucu-ambient
/// <see cref="ICurrentBranch"/>'e taşır (<c>WorkingCompanyContextProvider</c> deseninin aynası).
/// Default <c>NullBranchContextProvider</c>'ı değiştirir.
///
/// <para><b>MAYIN — kaynak singleton store'dur:</b> scoped <c>IWorkingContextService</c>'ten OKUNMAZ. ABP UoW
/// her AppService/DbContext çağrısı için CHILD DI scope yaratır; o scope'ta bu servisin BOŞ kopyası çözülür
/// (ölçümle kanıtlı) → ambient daima null'a düşerdi. Bkz. <c>WorkingCompanyContextProvider:49-56</c>.</para>
///
/// <para>Şube ambient'i bir query-filter'a bağlı değildir (şirketteki gibi izinli-kümeye indirgeme yoktur);
/// seçim zaten sunucu-filtreli <c>GetMyBranchesAsync</c> listesinden yapılır ve store'a yalnız sunucu yazar.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
public class WorkingBranchContextProvider : IBranchContextProvider, IScopedDependency
{
    private readonly WorkingSelectionStore _selectionStore;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public WorkingBranchContextProvider(
        WorkingSelectionStore selectionStore,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
    {
        _selectionStore = selectionStore;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public Guid? GetCurrentBranchId()
    {
        var entry = _currentUser.Id is { } userId
            ? _selectionStore.Get(_currentTenant.Id, userId)
            : null;

        return entry?.SelectedBranchId;
    }
}
