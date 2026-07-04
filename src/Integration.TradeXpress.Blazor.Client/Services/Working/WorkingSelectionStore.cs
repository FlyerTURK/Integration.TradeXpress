using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// Working-context seçiminin (seçili şirket + izinli şirket kümesi) <b>scope-bağımsız SSOT</b>'u —
/// per-user, process-içi, SINGLETON.
///
/// <para><b>Neden var (ölçümle kanıtlı kök neden):</b> <see cref="WorkingContextService"/> circuit-scoped ve
/// lazy; ABP <c>UnitOfWork</c> ise her AppService/DbContext çağrısı için CHILD DI scope yaratır. DbContext'in
/// company görünürlük filtresi <c>ICurrentCompany</c>'yi o child scope'tan çözünce WorkingContextService'in
/// BOŞ bir kopyasını görür (circuit'teki dolu instance değil) → effective şirket <see cref="Guid.Empty"/>
/// sentinel'e düşer → owned kayıtlar (Vault/Account/SubAccount) yazan kullanıcıya bile GÖRÜNMEZ olur
/// (örn. Account INSERT'i başarılı, hemen ardından SubAccount create parent Account'u "bulamadı").</para>
///
/// <para><b>Desen:</b> UI (WorkingContextService) yükleme/seçim değişiminde buraya YAZAR;
/// <see cref="WorkingCompanyContextProvider"/> hangi scope'ta çalışırsa çalışsın kullanıcı kimliğiyle OKUR.
/// Değer per-user olduğundan circuit/scope sayısından bağımsız tutarlıdır. İzinli küme de birlikte saklanır →
/// sunucu-taraflı yetki doğrulaması (yetkisiz seçim → ilk izinli) provider'da aynen çalışır.</para>
///
/// <para><b>Yaşam döngüsü:</b> process-içi cache'dir; login sonrası ilk UI yüklemesi doldurur, grant/seçim
/// değişince üzerine yazılır, restart'ta boşalır (UI yeniden doldurur). Tenant boyutu anahtardadır
/// (aynı kullanıcı Id'si farklı tenant'ta çakışmaz).</para>
/// </summary>
public class WorkingSelectionStore : ISingletonDependency
{
    /// <summary>Kullanıcının working seçimi: seçili şube→şirket + sunucu-filtreli izinli şirket kümesi.</summary>
    public sealed record Entry(Guid? SelectedCompanyId, IReadOnlyList<Guid> AllowedCompanyIds);

    private readonly ConcurrentDictionary<(Guid? TenantId, Guid UserId), Entry> _entries = new();

    public void Set(Guid? tenantId, Guid userId, Guid? selectedCompanyId, IReadOnlyList<Guid> allowedCompanyIds)
    {
        _entries[(tenantId, userId)] = new Entry(selectedCompanyId, allowedCompanyIds);
    }

    public Entry? Get(Guid? tenantId, Guid userId)
    {
        return _entries.TryGetValue((tenantId, userId), out var entry) ? entry : null;
    }

    /// <summary>Grant değişimi/çıkış gibi durumlarda kullanıcının kaydını düşürür (bir sonraki UI yüklemesi tazeler).</summary>
    public void Remove(Guid? tenantId, Guid userId)
    {
        _entries.TryRemove((tenantId, userId), out _);
    }
}
