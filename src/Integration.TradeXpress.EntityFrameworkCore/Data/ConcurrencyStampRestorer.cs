using System.Threading.Tasks;
using Integration.Framework.Data;
using Integration.TradeXpress.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;

namespace Integration.TradeXpress.Data;

/// <summary>
/// <see cref="IConcurrencyStampRestorer"/> implementasyonu — ambient unit-of-work'ün
/// DbContext'inde ABP'nin SaveChanges-öncesi stamp rotasyonunu (original←mevcut, mevcut←yeni)
/// geri sarar. Başarısız SaveChanges'te EF savepoint rollback'i VERİTABANINI eski stamp'e
/// döndürür; bellekteki mevcut stamp'i original'e geri sarmak retry'daki yeni rotasyonun
/// WHERE koşulunu veritabanıyla yeniden hizalar (kanıt: VoucherTransactionRollbackTests).
/// NOT: Framework'te EntityFrameworkCore projesi yok; DbContext'e erişen en merkezi katman
/// burası — Framework.EntityFrameworkCore doğarsa bu sınıf oraya taşınır (detector gibi).
/// </summary>
public class ConcurrencyStampRestorer : IConcurrencyStampRestorer, ITransientDependency
{
    private readonly IDbContextProvider<TradeXpressDbContext> _dbContextProvider;

    public ConcurrencyStampRestorer(IDbContextProvider<TradeXpressDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    public async Task RestoreRotatedStampsAsync()
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            // Yalnız UPDATE/DELETE komutları stamp'i WHERE koşuluna koyar; Added etkilenmez.
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is not IHasConcurrencyStamp entity)
            {
                continue;
            }

            // Original değer rotasyondan etkilenmez (başarısız SaveChanges accept edilmedi) —
            // veritabanının savepoint sonrası gerçek stamp'i budur; mevcut değeri ona geri sar.
            if (entry.Property(nameof(IHasConcurrencyStamp.ConcurrencyStamp)).OriginalValue
                is string originalStamp)
            {
                entity.ConcurrencyStamp = originalStamp;
            }
        }
    }
}
