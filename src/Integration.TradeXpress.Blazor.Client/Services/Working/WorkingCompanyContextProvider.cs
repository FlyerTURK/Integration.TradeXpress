using System;
using Integration.TradeXpress.MultiCompany;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Integration.TradeXpress.Blazor.Client.Services.Working;

/// <summary>
/// <see cref="ICompanyContextProvider"/> köprüsü — Blazor circuit'indeki <see cref="IWorkingContextService"/>'in
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
    private readonly IWorkingContextService _working;
    private readonly ILogger<WorkingCompanyContextProvider> _logger;

    public WorkingCompanyContextProvider(
        IWorkingContextService working,
        ILogger<WorkingCompanyContextProvider> logger)
    {
        _working = working;
        _logger = logger;
    }

    public Guid? GetCurrentCompanyId()
    {
        var selected = _working.CurrentCompanyId;
        var allowed = _working.AllowedCompanyIds;

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
