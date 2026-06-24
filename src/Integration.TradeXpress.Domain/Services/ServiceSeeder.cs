using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace Integration.TradeXpress.Services;

/// <summary>
/// Host (TenantId=null) Service (Hizmet) kataloğunu seed eder. Varsayılan hizmet tanımları
/// (işçilik/rafinaj/komisyon...). Idempotent (var olan kodu atlar). Yalnız host.
/// </summary>
public class ServiceSeeder(
    IRepository<Service, Guid> serviceRepository,
    IDataFilter dataFilter,
    ICurrentTenant currentTenant,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    // Gerçek hizmet listesi netleşince doldurulacak (fake örnek veri konmaz).
    private static readonly (string Code, string Name)[] Seeds = Array.Empty<(string, string)>();

    public async Task SeedAsync()
    {
        using (currentTenant.Change(null))
        using (dataFilter.Disable<IMultiTenant>())
        {
            var existing = (await serviceRepository.GetQueryableAsync())
                .Where(s => s.TenantId == null)
                .Select(s => s.Code)
                .ToList()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (code, name) in Seeds)
            {
                if (!existing.Contains(code))
                    await serviceRepository.InsertAsync(new Service(code, name), autoSave: false);
            }

            await unitOfWorkManager.Current!.SaveChangesAsync();
        }
    }
}
