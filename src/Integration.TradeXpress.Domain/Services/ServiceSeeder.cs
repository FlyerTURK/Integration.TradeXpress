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
/// Her şirkete sistem Service (Hizmet) kataloğunu seed eder — varsayılan hizmet tanımları
/// (işçilik/rafinaj/komisyon...). Idempotent (var olan kodu atlar).
/// <b>Host'ta ÇALIŞMAZ</b> (host'ta şirket yok; orchestrator tenant dalında çağrılır).
/// </summary>
public class ServiceSeeder(
    IRepository<Service, Guid> serviceRepository,
    IRepository<Company, Guid> companyRepository,
    IDataFilter dataFilter,
    IUnitOfWorkManager unitOfWorkManager)
    : ITransientDependency
{
    // Gerçek hizmet listesi netleşince doldurulacak (fake örnek veri konmaz).
    private static readonly (string Code, string Name)[] Seeds = Array.Empty<(string, string)>();

    /// <summary>Aktif tenant context'inde çalışır → eklenen Service'ler o tenant'ın HER şirketine yazılır.</summary>
    public async Task SeedAsync()
    {
        // PER-COMPANY (görev #4): hizmet kataloğu artık ŞİRKETE aittir (ICompanyOwned). Eskiden host-global
        // seed ediliyordu (yanlış katman) — hizmet VoucherLine'da emtia olarak seçiliyor, şirket sınırındadır.
        var companies = await companyRepository.GetListAsync();

        foreach (var company in companies)
        {
            // Soft-delete filtresi KAPALI: silinmiş kayıt da "mevcut" sayılır — silineni diriltme (MetalSeeder deseni).
            List<string> existingCodes;
            using (dataFilter.Disable<ISoftDelete>())
            {
                existingCodes = (await serviceRepository.GetQueryableAsync())
                    .Where(s => s.CompanyId == company.Id)
                    .Select(s => s.Code)
                    .ToList();
            }

            var existing = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var (code, name) in Seeds)
            {
                if (existing.Contains(code)) continue;
                await serviceRepository.InsertAsync(new Service(code, name, companyId: company.Id), autoSave: false);
            }
        }

        await unitOfWorkManager.Current!.SaveChangesAsync();
    }
}
