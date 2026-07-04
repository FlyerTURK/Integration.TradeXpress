using System;
using System.Linq;
using System.Threading.Tasks;
using Integration.Framework.Data;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace Integration.TradeXpress.Vouchers;

/// <summary>
/// VoucherNumber tahsisi: MAX(VoucherNumber for company) + 1 (lazy, ilk satırda) ve MAX+1 yarışının
/// tek noktadan yakalanması. Unique index (TenantId,CompanyId,VoucherNumber) veri bütünlüğünü zaten
/// korur; <see cref="InsertNumberedAsync"/> ihlalde numarayı MAX+1 ile YENİDEN hesaplayıp insert'i
/// şeffafça tekrarlar (kullanıcıya hata sızmaz); denemeler tükenirse lokalize "tekrar deneyin"
/// mesajına çevirir (panel verisi ekranda kalır, kullanıcı yeniden kaydeder).
/// </summary>
public class VoucherNumberAllocator : ITransientDependency
{
    /// <summary>Toplam insert denemesi (ilk deneme dahil). Aynı transaction içinde retry GÜVENLİ:
    /// EF Core aktif transaction'da SaveChanges öncesi savepoint açar, başarısızlıkta savepoint'e
    /// geri sarar (transaction poison olmaz, entity Added kalır); unique ihlali SQL Server'da
    /// statement-terminating'dir (XACT_ABORT OFF), Sqlite'ta da yalnız statement'ı iptal eder.</summary>
    private const int MaxInsertAttempts = 3;

    private readonly IRepository<Voucher, Guid> _repository;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IUniqueConstraintViolationDetector _uniqueViolationDetector;
    private readonly IConcurrencyStampRestorer _concurrencyStampRestorer;

    public VoucherNumberAllocator(
        IRepository<Voucher, Guid> repository,
        IAsyncQueryableExecuter asyncExecuter,
        IUniqueConstraintViolationDetector uniqueViolationDetector,
        IConcurrencyStampRestorer concurrencyStampRestorer)
    {
        _repository               = repository;
        _asyncExecuter            = asyncExecuter;
        _uniqueViolationDetector  = uniqueViolationDetector;
        _concurrencyStampRestorer = concurrencyStampRestorer;
    }

    public async Task<long> NextNumberAsync(Guid companyId)
    {
        var query = await _repository.GetQueryableAsync();
        var maxNumber = await _asyncExecuter.MaxAsync(
            query.Where(v => v.CompanyId == companyId).Select(v => (long?)v.VoucherNumber)) ?? 0L;
        return maxNumber + 1;
    }

    /// <summary>Numaralı fişi insert eder. VoucherNumber unique-index yarışında (MAX+1 bayatladı)
    /// numarayı yeniden hesaplayıp şeffafça tekrar dener; denemeler tükenirse lokalize hataya çevirir.
    /// Kalıcı çakışma (ör. soft-deleted satır index'te numarayı tutuyor, MAX görmüyor) her denemede
    /// aynı numarayı üretir ve tükenmeyle NumberConflict'e düşer — geriye-uyumlu davranış.</summary>
    public async Task InsertNumberedAsync(Voucher voucher)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _repository.InsertAsync(voucher, autoSave: true);
                return;
            }
            catch (Exception ex) when (IsVoucherNumberConflict(ex))
            {
                if (attempt >= MaxInsertAttempts)
                {
                    throw new BusinessException("TradeXpress:Voucher:NumberConflict");
                }

                // Başarısız SaveChanges savepoint'e geri sarıldı; entity hâlâ Added — yarışı kazanan
                // rakip commit'lediği için MAX+1 yeniden-hesabı taze numarayı görür. Aynı batch'teki
                // Modified entity'lerin ABP stamp rotasyonu da geri sarılmalı (yoksa retry'da sahte
                // DbUpdateConcurrencyException — bkz. IConcurrencyStampRestorer).
                await _concurrencyStampRestorer.RestoreRotatedStampsAsync();
                voucher.SetVoucherNumber(await NextNumberAsync(voucher.CompanyId));
            }
        }
    }

    /// <summary>VoucherNumber unique index (TenantId,CompanyId,VoucherNumber) ihlali mi? MAX+1 yarışında
    /// (iki kullanıcı aynı anda ilk satır) ikinci insert bu ihlale düşer. Birincil sınıflandırma
    /// sağlayıcı hata kodu (tip-güvenli, collation/lokalizasyondan bağımsız); index adı mesajda
    /// yalnız İKİNCİL daraltıcı kontrol olarak aranır.</summary>
    private bool IsVoucherNumberConflict(Exception ex)
    {
        return _uniqueViolationDetector.IsUniqueConstraintViolation(
            ex, constraintNameHint: nameof(Voucher.VoucherNumber));
    }
}
