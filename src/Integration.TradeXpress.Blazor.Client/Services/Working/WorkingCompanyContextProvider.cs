using System;
using Integration.TradeXpress.MultiCompany;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// <see cref="ICompanyContextProvider"/> uygulaması — Blazor circuit'indeki <see cref="IWorkingContextService"/>'in
/// çalışılan şirketini sunucu-ambient <see cref="ICurrentCompany"/>'ye taşır. Böylece AppService/DbContext
/// aktif şirketi DTO'dan değil ambient'ten okur (auto-stamp + merkezi filtre bununla çalışır).
/// Default <c>NullCompanyContextProvider</c>'ı değiştirir.
///
/// <para><b>ASIL GÜVENLİK AĞI (Faz 4 Adım 2 — 2b):</b> seçili şirketi filtreye vermeden ÖNCE izinli kümeye
/// (<see cref="IWorkingContextService.AllowedCompanyIds"/> — server-side resolver-filtreli <c>_branches</c>'ten
/// türer) karşı doğrular ve <see cref="WorkingCompanyScope"/> saf kuralıyla indirger: yetkisiz seçim → ilk
/// izinli şirket (null'a DEĞİL — null=konsolide=ters güvenlik); hiç izinli yoksa <see cref="Guid.Empty"/>
/// sentinel (owned kayıt görünmez, katalog görünür). Client'a güvenilmez; bu doğrulama sunucuda her sorguda
/// çalışır.</para>
///
/// <para><b>Bypass — kasıtlı ve korunmalı:</b> <c>ICurrentCompany.Change</c> (AsyncLocal override; seed /
/// cross-company sunucu işlemleri) bu provider'ı ATLAR → server-initiated işlemler bu doğrulamadan MUAF
/// (doğru davranış). Buradaki zorlama yalnız kullanıcı circuit'inin seçimi içindir.</para>
/// </summary>
[Dependency(ReplaceServices = true)]
public class WorkingCompanyContextProvider : ICompanyContextProvider, IScopedDependency
{
    private readonly WorkingSelectionStore _selectionStore;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<WorkingCompanyContextProvider> _logger;

    public WorkingCompanyContextProvider(
        WorkingSelectionStore selectionStore,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        ILogger<WorkingCompanyContextProvider> logger)
    {
        _selectionStore = selectionStore;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public Guid? GetCurrentCompanyId()
    {
        // SSOT: scope-bağımsız singleton store (per-user). Scoped IWorkingContextService'ten OKUNMAZ —
        // ABP UoW child scope'unda o servisin boş kopyası çözülür (ölçümle kanıtlı kök neden) ve tüm
        // owned kayıtlar sentinel'e düşerdi. Store'u UI (WorkingContextService) yükleme/seçim anında doldurur.
        // Henüz doldurulmamışsa (login sonrası ilk anlar) davranış ESKİYLE AYNI fail-safe'tir:
        // selected=null + allowed=boş → sentinel (owned gizli) — güvenlik sınırı gevşetilmez.
        var entry = _currentUser.Id is { } userId
            ? _selectionStore.Get(_currentTenant.Id, userId)
            : null;

        var selected = entry?.SelectedCompanyId;
        var allowed = entry?.AllowedCompanyIds ?? Array.Empty<Guid>();

        var effective = WorkingCompanyScope.ResolveEffectiveCompanyId(selected, allowed);

        if (WorkingCompanyScope.IsUnauthorizedSelection(selected, allowed))
        {
            _logger.LogWarning(
                "Working-context yetkisiz şirket seçimi reddedildi (Selected={Selected}) → efektif {Effective}.",
                selected,
                effective == Guid.Empty ? "erişim-yok(sentinel)" : effective.ToString());
        }

        return effective;
    }
}
